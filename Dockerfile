# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Optionally trust extra CA certs (e.g. a corporate TLS-inspection root) so restore and
# outbound HTTPS work behind a proxy. Empty by default (drop *.crt into ./certs to enable).
COPY certs/ /usr/local/share/ca-certificates/extra/
RUN update-ca-certificates

# Restore first (better layer caching)
COPY LineHfBot/LineHfBot.csproj LineHfBot/
RUN dotnet restore LineHfBot/LineHfBot.csproj

COPY LineHfBot/ LineHfBot/
RUN dotnet publish LineHfBot/LineHfBot.csproj -c Release -o /app --no-restore

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Same optional CA trust for outbound calls (Hugging Face / LINE) when run behind a proxy.
COPY certs/ /usr/local/share/ca-certificates/extra/
RUN update-ca-certificates

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Run as the non-root user provided by the base image.
USER app

ENTRYPOINT ["dotnet", "LineHfBot.dll"]
