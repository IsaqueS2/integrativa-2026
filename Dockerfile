FROM mcr.microsoft.com/dotnet/sdk:10.0.103-noble AS ocr-native
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates git cmake build-essential libtesseract-dev libc6-dev libjpeg-dev \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 --branch 1.85.0 https://github.com/DanBloomberg/leptonica.git /leptonica \
    && cmake -S /leptonica -B /leptonica/build -DBUILD_SHARED_LIBS=ON \
    && cmake --build /leptonica/build --config Release \
    && cmake --install /leptonica/build --prefix /usr/local \
    && mkdir -p /native/lib \
    && cp -a /usr/local/lib/liblept* /native/lib/

FROM mcr.microsoft.com/dotnet/sdk:10.0.103-noble AS build
WORKDIR /src

COPY global.json ./
COPY src/Confeitaria.Web/Confeitaria.Web.csproj src/Confeitaria.Web/
RUN dotnet restore src/Confeitaria.Web/Confeitaria.Web.csproj

COPY . .
RUN dotnet publish src/Confeitaria.Web/Confeitaria.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.3-noble AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libtesseract-dev libc6-dev libjpeg-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
COPY --from=ocr-native /native/lib/ /usr/local/lib/

EXPOSE 10000
RUN ldconfig \
    && mkdir -p /app/x64 \
    && ln -s /usr/local/lib/liblept.so /app/x64/libleptonica-1.85.0.dll.so \
    && ln -s /usr/lib/x86_64-linux-gnu/libtesseract.so /app/x64/libtesseract55.dll.so

ENTRYPOINT ["sh", "-c", "dotnet Confeitaria.Web.dll --urls http://0.0.0.0:${PORT:-10000}"]
