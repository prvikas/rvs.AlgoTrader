# Scaffolding: .csproj Templates

> Copy these exactly. Every `<RootNamespace>` and `<AssemblyName>` starts with `rvs.AlgoTrader`.
> All NuGet versions match the pinned versions in CLAUDE.md.

---

## Directory.Build.props (repo root)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>13.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>CS1591</NoWarn>  <!-- suppress missing XML doc warnings on non-API projects -->
  </PropertyGroup>
</Project>
```

---

## Directory.Packages.props (repo root — Central Package Management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Core -->
    <PackageVersion Include="MediatR" Version="12.*" />
    <PackageVersion Include="MassTransit.RabbitMQ" Version="8.*" />
    <PackageVersion Include="Polly" Version="8.*" />
    <PackageVersion Include="FluentValidation.AspNetCore" Version="11.*" />
    <PackageVersion Include="Serilog.AspNetCore" Version="8.*" />
    <PackageVersion Include="Serilog.Sinks.OpenTelemetry" Version="3.*" />
    <PackageVersion Include="NodaTime" Version="3.*" />
    <PackageVersion Include="NodaTime.Serialization.SystemTextJson" Version="3.*" />
    <PackageVersion Include="Hangfire.AspNetCore" Version="1.*" />
    <PackageVersion Include="Hangfire.PostgreSql" Version="1.*" />
    <PackageVersion Include="Riok.Mapperly" Version="3.*" />
    <PackageVersion Include="QuestPDF" Version="2024.*" />
    <PackageVersion Include="VaultSharp" Version="1.*" />
    <!-- EF Core + PostgreSQL -->
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Tools" Version="9.*" />
    <!-- Redis -->
    <PackageVersion Include="StackExchange.Redis" Version="2.*" />
    <!-- OpenTelemetry -->
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.*" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.*" />
    <PackageVersion Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.*" />
    <!-- Testing -->
    <PackageVersion Include="xunit" Version="2.*" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageVersion Include="Moq" Version="4.20.*" />
    <PackageVersion Include="FluentAssertions" Version="6.*" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="3.*" />
    <PackageVersion Include="Testcontainers.Redis" Version="3.*" />
    <PackageVersion Include="Testcontainers.RabbitMq" Version="3.*" />
    <PackageVersion Include="Respawn" Version="6.*" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.*" />
    <PackageVersion Include="Microsoft.Playwright" Version="1.*" />
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="9.*" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.Domain/rvs.AlgoTrader.Domain.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Domain</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Domain</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NodaTime" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.Application/rvs.AlgoTrader.Application.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Application</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="FluentValidation.AspNetCore" />
    <PackageReference Include="Riok.Mapperly" />
    <PackageReference Include="NodaTime" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.Infrastructure/rvs.AlgoTrader.Infrastructure.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Infrastructure</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Brokers.Abstractions\rvs.AlgoTrader.Brokers.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" />
    <PackageReference Include="StackExchange.Redis" />
    <PackageReference Include="MassTransit.RabbitMQ" />
    <PackageReference Include="Hangfire.AspNetCore" />
    <PackageReference Include="Hangfire.PostgreSql" />
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.OpenTelemetry" />
    <PackageReference Include="VaultSharp" />
    <PackageReference Include="Polly" />
    <PackageReference Include="NodaTime" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
    <PackageVersion Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.Brokers.Abstractions/rvs.AlgoTrader.Brokers.Abstractions.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Brokers.Abstractions</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Brokers.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="NodaTime" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.Brokers.Zerodha/rvs.AlgoTrader.Brokers.Zerodha.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Brokers.Zerodha</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Brokers.Zerodha</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Brokers.Abstractions\rvs.AlgoTrader.Brokers.Abstractions.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Polly" />
    <PackageReference Include="NodaTime" />
  </ItemGroup>
</Project>
```
<!-- rvs.AlgoTrader.Brokers.Upstox and rvs.AlgoTrader.Brokers.MStock: same structure as Zerodha above -->

---

## src/rvs.AlgoTrader.Strategies/rvs.AlgoTrader.Strategies.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Strategies</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Strategies</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="NodaTime" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.Backtesting/rvs.AlgoTrader.Backtesting.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Backtesting</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Backtesting</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Strategies\rvs.AlgoTrader.Strategies.csproj" />
    <!-- NO reference to Brokers.* — enforced by NetArchTest -->
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="QuestPDF" />
    <PackageReference Include="NodaTime" />
  </ItemGroup>
</Project>
```

---

## src/rvs.AlgoTrader.API/rvs.AlgoTrader.API.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.API</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.API</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn></NoWarn>  <!-- enable XML doc warnings for API project -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj" />
    <!-- Infrastructure referenced ONLY for DI registration in Program.cs -->
    <ProjectReference Include="..\rvs.AlgoTrader.Infrastructure\rvs.AlgoTrader.Infrastructure.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Brokers.Zerodha\rvs.AlgoTrader.Brokers.Zerodha.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Brokers.Upstox\rvs.AlgoTrader.Brokers.Upstox.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Brokers.MStock\rvs.AlgoTrader.Brokers.MStock.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Strategies\rvs.AlgoTrader.Strategies.csproj" />
    <ProjectReference Include="..\rvs.AlgoTrader.Backtesting\rvs.AlgoTrader.Backtesting.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Hangfire.AspNetCore" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

---

## tests/rvs.AlgoTrader.UnitTests/rvs.AlgoTrader.UnitTests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.UnitTests</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.UnitTests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj" />
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj" />
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.Infrastructure\rvs.AlgoTrader.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.Strategies\rvs.AlgoTrader.Strategies.csproj" />
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.Backtesting\rvs.AlgoTrader.Backtesting.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Moq" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="NetArchTest.Rules" />
  </ItemGroup>
</Project>
```

---

## tests/rvs.AlgoTrader.IntegrationTests/rvs.AlgoTrader.IntegrationTests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.IntegrationTests</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.IntegrationTests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.Infrastructure\rvs.AlgoTrader.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\rvs.AlgoTrader.API\rvs.AlgoTrader.API.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Testcontainers.Redis" />
    <PackageReference Include="Testcontainers.RabbitMq" />
    <PackageReference Include="Respawn" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.*" />
  </ItemGroup>
</Project>
```

---

## tests/rvs.AlgoTrader.Tests.UI/rvs.AlgoTrader.Tests.UI.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>rvs.AlgoTrader.Tests.UI</AssemblyName>
    <RootNamespace>rvs.AlgoTrader.Tests.UI</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.Playwright" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
</Project>
```

---

## rvs.AlgoTrader.sln (abbreviated — project list)

```
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Domain",
    "src\rvs.AlgoTrader.Domain\rvs.AlgoTrader.Domain.csproj", "{GUID-1}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Application",
    "src\rvs.AlgoTrader.Application\rvs.AlgoTrader.Application.csproj", "{GUID-2}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Infrastructure",
    "src\rvs.AlgoTrader.Infrastructure\rvs.AlgoTrader.Infrastructure.csproj", "{GUID-3}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Brokers.Abstractions",
    "src\rvs.AlgoTrader.Brokers.Abstractions\rvs.AlgoTrader.Brokers.Abstractions.csproj", "{GUID-4}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Brokers.Zerodha",
    "src\rvs.AlgoTrader.Brokers.Zerodha\rvs.AlgoTrader.Brokers.Zerodha.csproj", "{GUID-5}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Brokers.Upstox",
    "src\rvs.AlgoTrader.Brokers.Upstox\rvs.AlgoTrader.Brokers.Upstox.csproj", "{GUID-6}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Brokers.MStock",
    "src\rvs.AlgoTrader.Brokers.MStock\rvs.AlgoTrader.Brokers.MStock.csproj", "{GUID-7}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Strategies",
    "src\rvs.AlgoTrader.Strategies\rvs.AlgoTrader.Strategies.csproj", "{GUID-8}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Backtesting",
    "src\rvs.AlgoTrader.Backtesting\rvs.AlgoTrader.Backtesting.csproj", "{GUID-9}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.API",
    "src\rvs.AlgoTrader.API\rvs.AlgoTrader.API.csproj", "{GUID-10}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.UnitTests",
    "tests\rvs.AlgoTrader.UnitTests\rvs.AlgoTrader.UnitTests.csproj", "{GUID-11}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.IntegrationTests",
    "tests\rvs.AlgoTrader.IntegrationTests\rvs.AlgoTrader.IntegrationTests.csproj", "{GUID-12}"
Project("{FAE04EC0-...}") = "rvs.AlgoTrader.Tests.UI",
    "tests\rvs.AlgoTrader.Tests.UI\rvs.AlgoTrader.Tests.UI.csproj", "{GUID-13}"
```
> Replace `{GUID-N}` with actual GUIDs generated by `dotnet sln add` or Visual Studio.
