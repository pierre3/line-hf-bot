# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Optionally trust extra CA certs (e.g. a corporate TLS-inspection root) so restore and
# outbound HTTPS work behind a proxy. Empty by default (drop *.crt into ./certs to enable).
# update-ca-certificates merges them into /etc/ssl/certs/ca-certificates.crt, which the
# chiseled runtime stage copies in (it has no shell to run update-ca-certificates itself).
COPY certs/ /usr/local/share/ca-certificates/extra/
RUN update-ca-certificates

# Restore first (better layer caching)
COPY LineHfBot/LineHfBot.csproj LineHfBot/
RUN dotnet restore LineHfBot/LineHfBot.csproj

COPY LineHfBot/ LineHfBot/
RUN dotnet publish LineHfBot/LineHfBot.csproj -c Release -o /app --no-restore

# --- runtime ---
# Chiseled (distroless-style) Ubuntu image: no shell or package manager, non-root by
# default, and far fewer CVEs than the full runtime image. The "-extra" variant keeps
# ICU + tzdata so culture-aware formatting (e.g. Japanese) still works.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app

# Carry the CA bundle assembled in the build stage (includes any corporate root CA from
# ./certs) so outbound HTTPS to Hugging Face / LINE still trusts a proxy root. No
# update-ca-certificates here — the chiseled image has no shell.
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt

COPY --from=build /app .

# The base image already listens on 8080 (ASPNETCORE_HTTP_PORTS=8080) and runs as the
# non-root 'app' user, so no ASPNETCORE_URLS override or USER line is needed here.
EXPOSE 8080

ENTRYPOINT ["dotnet", "LineHfBot.dll"]
