# 1. Build (Dùng SDK 9.0)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy file project và restore
COPY *.csproj ./
RUN dotnet restore

# Copy toàn bộ code và build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# 2. Run (Dùng Runtime 9.0)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Mở cổng 8080
EXPOSE 8080

# Chạy ứng dụng
ENTRYPOINT ["dotnet", "UniMarket.dll"]