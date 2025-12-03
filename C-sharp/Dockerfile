# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy everything
COPY . .

# Restore and Publish the API
RUN dotnet restore "./dataAccess.Api/dataAccess.Api.csproj"
RUN dotnet publish "./dataAccess.Api/dataAccess.Api.csproj" -c Release -o /app/publish

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# --------------------------------------------------------
# ✅ INSTALL POSTGRES TOOLS (For Backup Button) & AI LIBS
# Clean up apt lists immediately to save storage (1GB limit)
# --------------------------------------------------------
RUN apt-get update && \
    apt-get install -y libgomp1 postgresql-client && \
    rm -rf /var/lib/apt/lists/*

# Create a non-root user (Required by Hugging Face Security)
RUN useradd -m -u 1000 user

# Copy build artifacts
COPY --from=build /app/publish .

# ✅ Copy Models (ONNX)
COPY --from=build /source/dataAccess.Api/Models ./Models

# ✅ ENSURE FAQ IS COPIED
# (Optional safety if not in .csproj)
# COPY --from=build /source/dataAccess/Data/faq.json ./Data/faq.json

# Set ownership
RUN chown -R user:user /app

# Switch to user
USER user
ENV HOME=/home/user \
    PATH=/home/user/.local/bin:$PATH

# --------------------------------------------------------
# ✅ HUGGING FACE PORT: 7860 (Strict Requirement)
# --------------------------------------------------------
ENV ASPNETCORE_URLS=http://+:7860
EXPOSE 7860

ENTRYPOINT ["dotnet", "dataAccess.Api.dll"]