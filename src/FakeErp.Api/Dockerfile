FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/FakeErp.Api/FakeErp.Api.csproj src/FakeErp.Api/
RUN dotnet restore src/FakeErp.Api/FakeErp.Api.csproj
COPY src/FakeErp.Api/ src/FakeErp.Api/
RUN dotnet publish src/FakeErp.Api/FakeErp.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV Urls=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FakeErp.Api.dll"]
