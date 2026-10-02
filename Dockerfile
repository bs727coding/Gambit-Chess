# Gambit online server (ASP.NET Core + SignalR).
#   docker build -t gambit-server .
#   docker run -p 8080:8080 -v gambit-data:/data gambit-server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props NuGet.config ./
COPY src/Gambit.Core/ src/Gambit.Core/
COPY src/Gambit.Online.Contracts/ src/Gambit.Online.Contracts/
COPY src/Gambit.Server/ src/Gambit.Server/
RUN dotnet publish src/Gambit.Server/Gambit.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    GAMBIT_DATA=/data
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Gambit.Server.dll"]
