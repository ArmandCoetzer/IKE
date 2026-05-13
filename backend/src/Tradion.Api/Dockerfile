FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY ["Tradion.Api.csproj", "./"]
RUN dotnet restore "Tradion.Api.csproj"

COPY . .
RUN dotnet publish "Tradion.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5020
EXPOSE 5020

ENTRYPOINT ["dotnet", "Tradion.Api.dll"]
