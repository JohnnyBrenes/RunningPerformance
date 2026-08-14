FROM mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1fc6e423f543119c406d24e2e687d67c569f18f04a37a8b0005d80ad0dcee80 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/backend/ ./src/backend/
RUN dotnet restore src/backend/RunningPerformance.Api/RunningPerformance.Api.csproj --locked-mode
RUN dotnet publish src/backend/RunningPerformance.Api/RunningPerformance.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    FreeTier__DatabaseWarningMb=300 \
    FreeTier__DatabaseBlockMb=400 \
    FreeTier__StorageWarningMb=700 \
    FreeTier__StorageBlockMb=850
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "RunningPerformance.Api.dll"]
