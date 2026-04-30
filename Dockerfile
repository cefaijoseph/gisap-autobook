# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY GisapAutobook/GisapAutobook.csproj GisapAutobook/
RUN dotnet restore GisapAutobook/GisapAutobook.csproj

COPY GisapAutobook/ GisapAutobook/
WORKDIR /src/GisapAutobook
RUN dotnet publish -c Release -o /app/publish --no-restore

# Install Playwright browsers inside the build stage so we can copy them
RUN dotnet tool install --global Microsoft.Playwright.CLI 2>/dev/null || true
RUN /app/publish/GisapAutobook playwright install chromium 2>/dev/null || true

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install Google Chrome (used by Playwright Channel="chrome" to bypass Cloudflare bot detection)
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    gnupg \
    ca-certificates \
    fonts-liberation \
    xdg-utils \
    && wget -q -O /tmp/google-chrome.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && apt-get install -y --no-install-recommends /tmp/google-chrome.deb \
    && rm /tmp/google-chrome.deb \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "GisapAutobook.dll"]
