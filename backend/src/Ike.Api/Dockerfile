FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Ike.Api.csproj", "./"]
RUN dotnet restore "Ike.Api.csproj"

COPY . .
RUN dotnet publish "Ike.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# QuestPDF/Skia needs Linux font discovery and real font files in slim runtime images.
# Without these packages hosted PDFs can render logos/shapes but drop all text.
RUN apt-get update && apt-get install -y --no-install-recommends \
    fontconfig \
    fonts-dejavu \
    libfontconfig1 \
    libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5020
EXPOSE 5020

ENTRYPOINT ["dotnet", "Ike.Api.dll"]
