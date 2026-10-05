using Gambit.App.Controls;
using Gambit.App.Services;
using Gambit.App.Theming;

namespace Gambit.App.Helpers;

/// <summary>The user's board settings, applied the same way on every page with a board.</summary>
public static class BoardSettings
{
    /// <summary>Applies the theme, pieces and display options from Settings to <paramref name="board"/>.</summary>
    public static void ApplyUserSettings(this ChessBoardControl board)
    {
        AppSettings s = App.Settings.Current;
        BoardTheme theme = CurrentTheme(s);
        if (board.Theme != theme) board.Theme = theme;
        if (board.PieceSet.Id != s.PieceSet) board.PieceSet = PieceSets.Get(s.PieceSet);
        board.ShowLegalMoves = s.ShowLegalMoves;
        board.ShowCoordinates = s.ShowCoordinates;
        board.HighlightLastMove = s.HighlightLastMove;
        board.AnimateMoves = s.AnimateMoves;
        board.AutoQueen = s.AutoQueen;
    }

    /// <summary>The board theme <paramref name="s"/> describes: built-in or custom, with the chosen highlight.</summary>
    public static BoardTheme CurrentTheme(AppSettings s) =>
        BoardThemes.ForSettings(s.BoardTheme, s.CustomLightSquare, s.CustomDarkSquare, s.HighlightColor);
}
