FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/rvs.AlgoTrader.Domain/rvs.AlgoTrader.Domain.csproj", "src/rvs.AlgoTrader.Domain/"]
COPY ["src/rvs.AlgoTrader.Application/rvs.AlgoTrader.Application.csproj", "src/rvs.AlgoTrader.Application/"]
COPY ["src/rvs.AlgoTrader.Infrastructure/rvs.AlgoTrader.Infrastructure.csproj", "src/rvs.AlgoTrader.Infrastructure/"]
COPY ["src/rvs.AlgoTrader.Brokers.Abstractions/rvs.AlgoTrader.Brokers.Abstractions.csproj", "src/rvs.AlgoTrader.Brokers.Abstractions/"]
COPY ["src/rvs.AlgoTrader.Brokers.Zerodha/rvs.AlgoTrader.Brokers.Zerodha.csproj", "src/rvs.AlgoTrader.Brokers.Zerodha/"]
COPY ["src/rvs.AlgoTrader.Brokers.Upstox/rvs.AlgoTrader.Brokers.Upstox.csproj", "src/rvs.AlgoTrader.Brokers.Upstox/"]
COPY ["src/rvs.AlgoTrader.Brokers.MStock/rvs.AlgoTrader.Brokers.MStock.csproj", "src/rvs.AlgoTrader.Brokers.MStock/"]
COPY ["src/rvs.AlgoTrader.Strategies/rvs.AlgoTrader.Strategies.csproj", "src/rvs.AlgoTrader.Strategies/"]
COPY ["src/rvs.AlgoTrader.Backtesting/rvs.AlgoTrader.Backtesting.csproj", "src/rvs.AlgoTrader.Backtesting/"]
COPY ["src/rvs.AlgoTrader.API/rvs.AlgoTrader.API.csproj", "src/rvs.AlgoTrader.API/"]

RUN dotnet restore "src/rvs.AlgoTrader.API/rvs.AlgoTrader.API.csproj"

COPY . .
WORKDIR "/src/src/rvs.AlgoTrader.API"
RUN dotnet build "rvs.AlgoTrader.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "rvs.AlgoTrader.API.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "rvs.AlgoTrader.API.dll"]
