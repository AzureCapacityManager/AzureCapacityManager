# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/CapacityManager.Api/CapacityManager.Api.csproj src/CapacityManager.Api/
RUN dotnet restore src/CapacityManager.Api/CapacityManager.Api.csproj

COPY src/CapacityManager.Api/ src/CapacityManager.Api/
RUN dotnet publish src/CapacityManager.Api/CapacityManager.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CapacityManager.Api.dll"]
