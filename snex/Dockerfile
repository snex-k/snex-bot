FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY snex.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
COPY config ./config

ENTRYPOINT ["dotnet", "snex.dll"]
