# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy everything (Root context)
COPY . .

# --------------------------------------------------------
# ✅ PATH FIX: Added "C-sharp/" prefix
# Since Dockerfile is now at Root, we must point inside the folder
# --------------------------------------------------------
RUN dotnet restore "./C-sharp/dataAccess.Api/dataAccess.Api.csproj"
RUN dotnet publish "./C-sharp/dataAccess.Api/dataAccess.Api.csproj" -c Release -o /app/publish

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# --------------------------------------------------------
# ✅ INSTALL POSTGRES TOOLS (For Backup Button) & AI LIBS
# --------------------------------------------------------
RUN apt-get update && \
    apt-get install -y libgomp1 postgresql-client && \
    rm -rf /var/lib/apt/lists/*

# Create a non-root user (Required by Hugging Face Security)
RUN useradd -m -u 1000 user

# Copy build artifacts
# (Includes Plugins & Models automatically due to .csproj update)
COPY --from=build /app/publish .

# Set ownership
RUN chown -R user:user /app

# Switch to user
USER user
ENV HOME=/home/user \
    PATH=/home/user/.local/bin:$PATH

# --------------------------------------------------------
# ✅ HUGGING FACE PORT: 7860
# --------------------------------------------------------
ENV ASPNETCORE_URLS=http://+:7860
EXPOSE 7860

ENTRYPOINT ["dotnet", "dataAccess.Api.dll"]