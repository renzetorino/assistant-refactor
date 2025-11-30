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

# Install system libraries for your AI (ONNX)
RUN apt-get update && apt-get install -y libgomp1 && rm -rf /var/lib/apt/lists/*

# Create a non-root user (Required by Hugging Face Security)
RUN useradd -m -u 1000 user

# Copy build artifacts
COPY --from=build /app/publish .

# Copy the Models folder (Crucial for your LocalEmbeddingService)
# Ensure this folder exists in your C-sharp directory before uploading!
COPY --from=build /source/dataAccess.Api/Models ./Models

# Set ownership to the non-root user
RUN chown -R user:user /app

# Switch to user
USER user
ENV HOME=/home/user \
    PATH=/home/user/.local/bin:$PATH

# Configure Network (Must be 7860)
ENV ASPNETCORE_URLS=http://+:7860
EXPOSE 7860

ENTRYPOINT ["dotnet", "dataAccess.Api.dll"]