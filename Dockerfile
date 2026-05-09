
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SphereBackend/SphereBackend.csproj SphereBackend/
RUN dotnet restore SphereBackend/SphereBackend.csproj

COPY SphereBackend/ SphereBackend/
RUN dotnet publish SphereBackend/SphereBackend.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENV PORT=10000

EXPOSE 10000

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SphereBackend.dll"]
