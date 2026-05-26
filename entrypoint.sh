#!/bin/sh
set -e

APP_DIR="/app"
IMAGE_APP_DATA="$APP_DIR/App_Data"
PERSISTENT_APP_DATA="/home/site/wwwroot/App_Data"

echo "Preparing persistent App_Data..."

mkdir -p "$PERSISTENT_APP_DATA"

# Seed persistent App_Data only the first time
if [ -z "$(ls -A "$PERSISTENT_APP_DATA" 2>/dev/null)" ]; then
  echo "Seeding App_Data from image..."
  cp -a "$IMAGE_APP_DATA/." "$PERSISTENT_APP_DATA/" || true
fi

# Preserve original image App_Data as a backup if it still exists as a real directory
if [ -d "$IMAGE_APP_DATA" ] && [ ! -L "$IMAGE_APP_DATA" ]; then
  mv "$IMAGE_APP_DATA" "$APP_DIR/App_Data.image"
fi

# Point nopCommerce App_Data to persistent storage
ln -sfn "$PERSISTENT_APP_DATA" "$IMAGE_APP_DATA"

chmod -R u+rwX,g+rwX "$PERSISTENT_APP_DATA" || true

echo "App_Data now points to:"
ls -la "$IMAGE_APP_DATA" || true
readlink -f "$IMAGE_APP_DATA" || true

echo "Testing App_Data write access..."
echo "write test $(date)" > "$IMAGE_APP_DATA/write-test.txt" \
  && echo "App_Data write OK" \
  || echo "App_Data write FAILED"

echo "plugins.json locations:"
find / -name plugins.json 2>/dev/null || true

exec dotnet Nop.Web.dll