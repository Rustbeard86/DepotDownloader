#!/bin/bash

# Backfill Preview Images Script
# Downloads preview images from Steam and uploads to GitHub /images/ directory
# for releases that don't have images yet.

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

# Config paths
OPT_DIR="/opt/github-archiver"
CONFIG_FILE="$OPT_DIR/appsettings.json"

# Read config
if [ ! -f "$CONFIG_FILE" ]; then
    echo -e "${RED}Config file not found: $CONFIG_FILE${NC}"
    exit 1
fi

GITHUB_TOKEN=$(grep -o '"Token":[^,]*' "$CONFIG_FILE" | head -1 | sed 's/.*"\([^"]*\)".*/\1/' | tail -1)
GITHUB_OWNER=$(grep -o '"Owner":[^,]*' "$CONFIG_FILE" | head -1 | sed 's/.*"\([^"]*\)".*/\1/' | tail -1)
GITHUB_REPO=$(grep -o '"Repository":[^,]*' "$CONFIG_FILE" | head -1 | sed 's/.*"\([^"]*\)".*/\1/' | tail -1)
GITHUB_BRANCH=$(grep -o '"Branch":[^,]*' "$CONFIG_FILE" | head -1 | sed 's/.*"\([^"]*\)".*/\1/' | tail -1)

if [ -z "$GITHUB_BRANCH" ]; then
    GITHUB_BRANCH="main"
fi

if [ -z "$GITHUB_TOKEN" ] || [ -z "$GITHUB_OWNER" ] || [ -z "$GITHUB_REPO" ]; then
    echo -e "${RED}Could not read GitHub config from $CONFIG_FILE${NC}"
    exit 1
fi

echo -e "${CYAN}=== Backfill Preview Images ===${NC}"
echo -e "Repository: ${GITHUB_OWNER}/${GITHUB_REPO}"
echo -e "Branch: ${GITHUB_BRANCH}"
echo ""

# Get existing images
echo -e "${CYAN}Fetching existing images from /images/ directory...${NC}"
existing_images=$(curl -s -H "Authorization: Bearer $GITHUB_TOKEN" \
    "https://api.github.com/repos/$GITHUB_OWNER/$GITHUB_REPO/contents/images?ref=$GITHUB_BRANCH" 2>/dev/null \
    | grep -o '"name": *"[^"]*"' | sed 's/.*"\([^"]*\)"/\1/' || echo "")

existing_count=$(echo "$existing_images" | grep -c . 2>/dev/null || echo "0")
echo -e "Found ${GREEN}${existing_count}${NC} existing images"

# Get all releases
echo -e "${CYAN}Fetching releases from GitHub...${NC}"
all_releases=""
page=1
while true; do
    response=$(curl -s -H "Authorization: Bearer $GITHUB_TOKEN" \
        "https://api.github.com/repos/$GITHUB_OWNER/$GITHUB_REPO/releases?per_page=100&page=$page")
    
    tags=$(echo "$response" | grep -o '"tag_name": *"[^"]*"' | sed 's/.*"\([^"]*\)"/\1/')
    count=$(echo "$tags" | grep -c . 2>/dev/null || echo "0")
    
    if [ "$count" -eq 0 ]; then
        break
    fi
    
    all_releases="$all_releases $tags"
    
    if [ "$count" -lt 100 ]; then
        break
    fi
    page=$((page + 1))
done

release_count=$(echo "$all_releases" | wc -w)
echo -e "Found ${GREEN}${release_count}${NC} releases"
echo ""

# Find releases missing images
missing_ids=""
for tag in $all_releases; do
    # Skip non-numeric tags
    if ! [[ "$tag" =~ ^[0-9]+$ ]]; then
        continue
    fi
    
    # Check if image exists (any extension)
    has_image=false
    for ext in jpg png gif webp; do
        if echo "$existing_images" | grep -q "^${tag}\.${ext}$"; then
            has_image=true
            break
        fi
    done
    
    if [ "$has_image" = false ]; then
        missing_ids="$missing_ids $tag"
    fi
done

missing_count=$(echo "$missing_ids" | wc -w)
echo -e "Releases missing images: ${YELLOW}${missing_count}${NC}"

if [ "$missing_count" -eq 0 ]; then
    echo -e "${GREEN}All releases have images!${NC}"
    exit 0
fi

echo ""
echo -e "${YELLOW}This will download $missing_count images from Steam and upload to GitHub.${NC}"
read -p "Continue? [y/N]: " confirm
if [[ ! "$confirm" =~ ^[yY]$ ]]; then
    echo "Cancelled."
    exit 0
fi

echo ""

# Process each missing image
uploaded=0
failed=0
skipped=0

for workshop_id in $missing_ids; do
    echo -e -n "Processing ${workshop_id}... "
    
    # Get Steam metadata
    steam_response=$(curl -s "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/" \
        -d "itemcount=1" -d "publishedfileids[0]=$workshop_id" 2>/dev/null)
    
    preview_url=$(echo "$steam_response" | grep -o '"preview_url": *"[^"]*"' | head -1 | sed 's/.*"\(http[^"]*\)".*/\1/')
    
    if [ -z "$preview_url" ]; then
        echo -e "${YELLOW}no preview URL${NC}"
        skipped=$((skipped + 1))
        continue
    fi
    
    # Download image to temp file
    temp_file="/tmp/${workshop_id}_preview"
    http_code=$(curl -s -w "%{http_code}" -o "$temp_file" "$preview_url")
    
    if [ "$http_code" != "200" ]; then
        echo -e "${RED}download failed (HTTP $http_code)${NC}"
        rm -f "$temp_file"
        failed=$((failed + 1))
        continue
    fi
    
    # Detect image type
    mime_type=$(file -b --mime-type "$temp_file" 2>/dev/null)
    case "$mime_type" in
        image/png) ext=".png" ;;
        image/gif) ext=".gif" ;;
        image/webp) ext=".webp" ;;
        *) ext=".jpg" ;;
    esac
    
    image_name="${workshop_id}${ext}"
    
    # Base64 encode to file (avoid command line length limits)
    base64_file="/tmp/${workshop_id}_base64"
    base64 -w 0 "$temp_file" > "$base64_file"
    rm -f "$temp_file"
    
    # Create JSON payload file
    json_file="/tmp/${workshop_id}_payload.json"
    printf '{"message":"Add image for %s","content":"' "$workshop_id" > "$json_file"
    cat "$base64_file" >> "$json_file"
    printf '","branch":"%s"}' "$GITHUB_BRANCH" >> "$json_file"
    rm -f "$base64_file"
    
    # Upload to GitHub using payload file
    upload_response=$(curl -s -X PUT \
        -H "Authorization: Bearer $GITHUB_TOKEN" \
        -H "Accept: application/vnd.github.v3+json" \
        -H "Content-Type: application/json" \
        "https://api.github.com/repos/$GITHUB_OWNER/$GITHUB_REPO/contents/images/$image_name" \
        -d @"$json_file")
    
    rm -f "$json_file"
    
    if echo "$upload_response" | grep -q '"sha"'; then
        echo -e "${GREEN}uploaded${NC}"
        uploaded=$((uploaded + 1))
    else
        error=$(echo "$upload_response" | grep -o '"message": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"/\1/')
        echo -e "${RED}failed: $error${NC}"
        failed=$((failed + 1))
    fi
    
    # Rate limit
    sleep 0.5
done

echo ""
echo -e "${CYAN}=== Summary ===${NC}"
echo -e "  Uploaded: ${GREEN}${uploaded}${NC}"
echo -e "  Skipped:  ${YELLOW}${skipped}${NC}"
echo -e "  Failed:   ${RED}${failed}${NC}"

if [ "$uploaded" -gt 0 ]; then
    echo ""
    echo -e "${GREEN}Done! Run option 19 (Rebuild manifest) to update workshopcontent.json with new image URLs.${NC}"
fi
