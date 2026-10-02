namespace Gambit.Core.Rating;

/// <summary>A Glicko-2 rating on the familiar 1500-centered scale.</summary>
public readonly record struct Glicko2Rating(double Rating, double Deviation, double Volatility)
{
    public static readonly Glicko2Rating Default = new(1500, 350, 0.06);

    /// <summary>Lower bound of the ~95% confidence interval.</summary>
    public double Low => Rating - 2 * Deviation;

    public double High => Rating + 2 * Deviation;

    /// <summary>Lichess calls a rating provisional while RD is above 110.</summary>
    public bool IsProvisional => Deviation > 110;

    public int Rounded => (int)Math.Round(Rating);

    public override string ToString() => IsProvisional ? $"{Rounded}?" : $"{Rounded}";
}

/// <summary>Glicko-2 (Glickman, 2013). Shared by puzzle ratings and online game ratings.</summary>
public static class Glicko2
{
    private const double Scale = 173.7178;
    private const double Epsilon = 0.000001;

    /// <summary>System constant τ: smaller = volatility changes more slowly.</summary>
    public const double DefaultTau = 0.5;

    public static double ExpectedScore(Glicko2Rating player, Glicko2Rating opponent)
    {
        double mu = (player.Rating - 1500) / Scale;
        double muJ = (opponent.Rating - 1500) / Scale;
        double phiJ = opponent.Deviation / Scale;
        return E(mu, muJ, phiJ);
    }

    /// <summary>Rate a single game. <paramref name="score"/>: 1 win, 0.5 draw, 0 loss.</summary>
    public static Glicko2Rating Update(Glicko2Rating player, Glicko2Rating opponent, double score,
        double tau = DefaultTau, double minDeviation = 45, double maxDeviation = 350) =>
        Update(player, [(opponent, score)], tau, minDeviation, maxDeviation);

    public static Glicko2Rating Update(Glicko2Rating player, IReadOnlyList<(Glicko2Rating Opponent, double Score)> results,
        double tau = DefaultTau, double minDeviation = 45, double maxDeviation = 350)
    {
        double mu = (player.Rating - 1500) / Scale;
        double phi = player.Deviation / Scale;
        double sigma = player.Volatility;

        if (results.Count == 0)
        {
            double idlePhi = Math.Sqrt(phi * phi + sigma * sigma);
            return player with { Deviation = Math.Clamp(idlePhi * Scale, minDeviation, maxDeviation) };
        }

        double vInv = 0, deltaSum = 0;
        foreach (var (opp, s) in results)
        {
            double muJ = (opp.Rating - 1500) / Scale;
            double phiJ = opp.Deviation / Scale;
            double g = G(phiJ);
            double e = E(mu, muJ, phiJ);
            vInv += g * g * e * (1 - e);
            deltaSum += g * (s - e);
        }

        double v = 1 / vInv;
        double delta = v * deltaSum;

        // Step 5: new volatility via the Illinois algorithm.
        double a = Math.Log(sigma * sigma);
        double F(double x)
        {
            double ex = Math.Exp(x);
            double num = ex * (delta * delta - phi * phi - v - ex);
            double den = 2 * Math.Pow(phi * phi + v + ex, 2);
            return num / den - (x - a) / (tau * tau);
        }

        double A = a, B;
        if (delta * delta > phi * phi + v)
        {
            B = Math.Log(delta * delta - phi * phi - v);
        }
        else
        {
            int k = 1;
            while (F(a - k * tau) < 0) k++;
            B = a - k * tau;
        }

        double fA = F(A), fB = F(B);
        for (int i = 0; i < 100 && Math.Abs(B - A) > Epsilon; i++)
        {
            double C = A + (A - B) * fA / (fB - fA);
            double fC = F(C);
            if (fC * fB <= 0)
            {
                A = B;
                fA = fB;
            }
            else
            {
                fA /= 2;
            }
            B = C;
            fB = fC;
        }

        double newSigma = Math.Exp(A / 2);
        double phiStar = Math.Sqrt(phi * phi + newSigma * newSigma);
        double newPhi = 1 / Math.Sqrt(1 / (phiStar * phiStar) + 1 / v);
        double newMu = mu + newPhi * newPhi * deltaSum;

        return new Glicko2Rating(
            newMu * Scale + 1500,
            Math.Clamp(newPhi * Scale, minDeviation, maxDeviation),
            newSigma);
    }

    private static double G(double phi) => 1 / Math.Sqrt(1 + 3 * phi * phi / (Math.PI * Math.PI));

    private static double E(double mu, double muJ, double phiJ) => 1 / (1 + Math.Exp(-G(phiJ) * (mu - muJ)));
}
