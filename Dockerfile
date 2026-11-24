# ---------- збірка ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/NbuRates/NbuRates.csproj src/NbuRates/
RUN dotnet restore src/NbuRates/NbuRates.csproj
COPY src/ src/
RUN dotnet publish src/NbuRates/NbuRates.csproj -c Release -o /app --no-restore

# ---------- запуск ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .

# БД зберігається у томі /data, щоб переживати перезапуск контейнера.
# Рядок підключення перевизначено змінною середовища — код не змінюється.
ENV ConnectionStrings__Sqlite="Data Source=/data/nbu_rates.db"
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
USER $APP_UID

ENTRYPOINT ["dotnet", "NbuRates.dll"]
