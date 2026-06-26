# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["QuanLyKhachSan.csproj", "./"]
RUN dotnet restore "QuanLyKhachSan.csproj"

# Copy source code and publish
COPY . .
RUN dotnet publish "QuanLyKhachSan.csproj" -c Release -o /app/publish --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create logs directory
RUN mkdir -p /app/logs

# Copy published output
COPY --from=build /app/publish .

# Expose HTTP port
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "QuanLyKhachSan.dll"]
