# syntax=docker/dockerfile:1

# Build stage: context is the repository root so the embedded sample data is available.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/src/ backend/src/
COPY data/ data/

RUN dotnet publish backend/src/TreeEditor.Api/TreeEditor.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5080
ENTRYPOINT ["dotnet", "TreeEditor.Api.dll"]
