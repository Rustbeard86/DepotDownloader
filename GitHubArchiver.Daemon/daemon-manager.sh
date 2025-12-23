#!/bin/bash

# GitHub Archiver Daemon Management Script
# Interactive control for github-archiver service

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Service name
SERVICE="github-archiver"

# Paths
DATA_DIR="/var/lib/github-archiver"
OPT_DIR="/opt/github-archiver"
DB_FILE="$DATA_DIR/workshop.db"

print_header() {
    echo -e "${CYAN}"
    echo "+================================================================+"
    echo "|           GitHub Archiver Daemon Manager                      |"
    echo "|        Steam Workshop -> GitHub Content Archiver              |"
    echo "+================================================================+"
    echo -e "${NC}"
}

print_status() {
    echo -e "${BLUE}------------------------------------------------------------------${NC}"
    echo -e "${CYAN}Service Status:${NC}  $(date '+%H:%M:%S')"
    echo ""
    
    if systemctl is-active --quiet $SERVICE; then
        local uptime=$(systemctl show $SERVICE --property=ActiveEnterTimestamp --value)
        echo -e "  GitHub Archiver:    ${GREEN}[Running]${NC} since $uptime"
        
        # Get memory usage
        local pid=$(systemctl show $SERVICE --property=MainPID --value)
        if [ "$pid" != "0" ]; then
            local mem=$(ps -p $pid -o rss= 2>/dev/null | awk '{printf "%.1f MB", $1/1024}')
            echo -e "  Memory Usage:       ${mem}"
        fi
    else
        echo -e "  GitHub Archiver:    ${RED}[Stopped]${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}Database Stats:${NC}"
    
    if [ -f "$DB_FILE" ]; then
        local total_items=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM WorkshopItems;" 2>/dev/null || echo "0")
        local archived_items=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM WorkshopItems WHERE ArchivedAt IS NOT NULL;" 2>/dev/null || echo "0")
        local pending_items=$((total_items - archived_items))
        local failed_downloads=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM FailedDownloads;" 2>/dev/null || echo "0")
        
        echo -e "  Total Items:        ${total_items}"
        echo -e "  Archived:           ${GREEN}${archived_items}${NC}"
        echo -e "  Pending:            ${YELLOW}${pending_items}${NC}"
        echo -e "  Failed:             ${RED}${failed_downloads}${NC}"
        
        # Show last archived item
        local last_archived=$(sqlite3 "$DB_FILE" "SELECT Title FROM WorkshopItems WHERE ArchivedAt IS NOT NULL ORDER BY ArchivedAt DESC LIMIT 1;" 2>/dev/null || echo "N/A")
        if [ -n "$last_archived" ] && [ "$last_archived" != "N/A" ]; then
            echo -e "  Last Archived:      ${last_archived:0:40}"
        fi
    else
        echo -e "  Database:           ${YELLOW}Not found${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}Disk Usage:${NC}"
    
    if [ -d "$OPT_DIR/depots" ]; then
        local depot_size=$(du -sh "$OPT_DIR/depots" 2>/dev/null | cut -f1)
        echo -e "  Depot Cache:        ${depot_size:-0}"
    fi
    
    if [ -d "$DATA_DIR/downloads" ]; then
        local download_size=$(du -sh "$DATA_DIR/downloads" 2>/dev/null | cut -f1)
        echo -e "  Downloads:          ${download_size:-0}"
    fi
    
    local tmp_zips=$(ls -1 /tmp/*.zip 2>/dev/null | wc -l || echo "0")
    echo -e "  Temp ZIPs:          ${tmp_zips} files"
    
    echo ""
    echo -e "${CYAN}Configuration:${NC}"
    if [ -f "$OPT_DIR/appsettings.json" ]; then
        local app_id=$(grep -o '"AppId":[^,]*' "$OPT_DIR/appsettings.json" | head -1 | grep -o '[0-9]*')
        local repo=$(grep -o '"Repository":[^,]*' "$OPT_DIR/appsettings.json" | head -1 | sed 's/.*"\([^"]*\)".*/\1/')
        local owner=$(grep -o '"Owner":[^,]*' "$OPT_DIR/appsettings.json" | head -1 | sed 's/.*"\([^"]*\)".*/\1/')
        echo -e "  Steam AppId:        ${app_id:-Unknown}"
        echo -e "  GitHub Repo:        ${owner:-?}/${repo:-Unknown}"
    fi
    
    echo -e "${BLUE}------------------------------------------------------------------${NC}"
}

print_menu() {
    echo ""
    echo -e "${YELLOW}Select an action:${NC}"
    echo ""
    echo "  Service Control:"
    echo "    1) Start service"
    echo "    2) Stop service"
    echo "    3) Restart service"
    echo "    4) Enable auto-start on boot"
    echo "    5) Disable auto-start on boot"
    echo ""
    echo "  Logs & Monitoring:"
    echo "    6) View logs (live)"
    echo "    7) View last 100 log entries"
    echo "    8) View errors only"
    echo "    9) View today's logs"
    echo ""
    echo "  Database:"
    echo "   10) Show recent archived items"
    echo "   11) Show pending items"
    echo "   12) Show failed downloads"
    echo "   13) Search items by name"
    echo "   14) Export manifest to file"
    echo ""
    echo "  Reset & Cleanup:"
    echo "   15) Clear failed downloads"
    echo "   16) Clear depot cache"
    echo "   17) Clear temp files"
    echo "   18) Full reset (WARNING: deletes all data)"
    echo ""
    echo "  Deployment:"
    echo "   19) Update binary from local build"
    echo "   20) Edit configuration"
    echo "   21) Backup database"
    echo ""
    echo "  Other:"
    echo "   r) Refresh status"
    echo "   0) Exit"
    echo ""
}

confirm() {
    local prompt="$1"
    echo -e -n "${YELLOW}${prompt} [y/N]: ${NC}"
    read -r response
    case "$response" in
        [yY][eE][sS]|[yY]) return 0 ;;
        *) return 1 ;;
    esac
}

start_service() {
    echo -e "${GREEN}Starting GitHub Archiver...${NC}"
    systemctl start $SERVICE
    sleep 2
    if systemctl is-active --quiet $SERVICE; then
        echo -e "${GREEN}Service started successfully!${NC}"
    else
        echo -e "${RED}Failed to start service. Check logs with option 6.${NC}"
    fi
}

stop_service() {
    echo -e "${YELLOW}Stopping GitHub Archiver...${NC}"
    systemctl stop $SERVICE 2>/dev/null || true
    echo -e "${GREEN}Service stopped.${NC}"
}

restart_service() {
    echo -e "${YELLOW}Restarting GitHub Archiver...${NC}"
    systemctl restart $SERVICE
    sleep 2
    if systemctl is-active --quiet $SERVICE; then
        echo -e "${GREEN}Service restarted successfully!${NC}"
    else
        echo -e "${RED}Failed to restart service. Check logs with option 6.${NC}"
    fi
}

enable_service() {
    systemctl enable $SERVICE
    echo -e "${GREEN}Auto-start enabled.${NC}"
}

disable_service() {
    systemctl disable $SERVICE
    echo -e "${YELLOW}Auto-start disabled.${NC}"
}

view_logs_live() {
    echo -e "${CYAN}Viewing live logs (Ctrl+C to exit)...${NC}"
    journalctl -u $SERVICE -f
}

view_logs_recent() {
    echo -e "${CYAN}Last 100 log entries:${NC}"
    journalctl -u $SERVICE -n 100 --no-pager
}

view_logs_errors() {
    echo -e "${CYAN}Error logs:${NC}"
    journalctl -u $SERVICE -p err --no-pager -n 50
}

view_logs_today() {
    echo -e "${CYAN}Today's logs:${NC}"
    journalctl -u $SERVICE --since today --no-pager
}

show_recent_archived() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Recently Archived Items (last 25):${NC}"
    echo ""
    sqlite3 -header -column "$DB_FILE" \
        "SELECT PublishedFileId as ID, 
                substr(Title, 1, 35) as Title,
                datetime(ArchivedAt, 'unixepoch', 'localtime') as ArchivedAt
         FROM WorkshopItems 
         WHERE ArchivedAt IS NOT NULL
         ORDER BY ArchivedAt DESC 
         LIMIT 25;"
}

show_pending_items() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Pending Items (not yet archived):${NC}"
    echo ""
    local count=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM WorkshopItems WHERE ArchivedAt IS NULL;" 2>/dev/null || echo "0")
    echo -e "Total pending: ${YELLOW}${count}${NC}"
    echo ""
    sqlite3 -header -column "$DB_FILE" \
        "SELECT PublishedFileId as ID, 
                substr(Title, 1, 40) as Title,
                FileSize / 1024 / 1024 as 'Size(MB)'
         FROM WorkshopItems 
         WHERE ArchivedAt IS NULL
         ORDER BY PublishedFileId DESC 
         LIMIT 20;"
}

show_failed_downloads() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Failed Downloads:${NC}"
    echo ""
    sqlite3 -header -column "$DB_FILE" \
        "SELECT PublishedFileId as ID, 
                Attempts,
                substr(LastError, 1, 40) as Error,
                datetime(LastAttempt, 'unixepoch', 'localtime') as LastAttempt
         FROM FailedDownloads 
         ORDER BY LastAttempt DESC;"
}

search_items() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    echo -n "Enter search term: "
    read -r search_term
    
    if [ -z "$search_term" ]; then
        echo -e "${YELLOW}No search term provided.${NC}"
        return
    fi
    
    echo -e "${CYAN}Search results for '${search_term}':${NC}"
    echo ""
    sqlite3 -header -column "$DB_FILE" \
        "SELECT PublishedFileId as ID, 
                substr(Title, 1, 35) as Title,
                CASE WHEN ArchivedAt IS NOT NULL THEN 'Yes' ELSE 'No' END as Archived
         FROM WorkshopItems 
         WHERE Title LIKE '%${search_term}%'
         ORDER BY Title
         LIMIT 30;"
}

export_manifest() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    local output_file="/tmp/workshop_manifest_$(date +%Y%m%d_%H%M%S).json"
    
    echo -e "${CYAN}Exporting archived items to ${output_file}...${NC}"
    
    sqlite3 "$DB_FILE" \
        "SELECT json_group_array(json_object(
            'id', PublishedFileId,
            'title', Title,
            'archivedAt', ArchivedAt
         ))
         FROM WorkshopItems 
         WHERE ArchivedAt IS NOT NULL;" > "$output_file"
    
    echo -e "${GREEN}Exported to ${output_file}${NC}"
    echo -e "Items exported: $(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM WorkshopItems WHERE ArchivedAt IS NOT NULL;")"
}

clear_failed_downloads() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    local count=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM FailedDownloads;" 2>/dev/null || echo "0")
    
    if [ "$count" -eq 0 ]; then
        echo -e "${YELLOW}No failed downloads to clear.${NC}"
        return
    fi
    
    if ! confirm "Clear $count failed download records?"; then
        echo "Cancelled."
        return
    fi
    
    sqlite3 "$DB_FILE" "DELETE FROM FailedDownloads;"
    echo -e "${GREEN}Cleared $count failed download records.${NC}"
}

clear_depot_cache() {
    if [ ! -d "$OPT_DIR/depots" ]; then
        echo -e "${YELLOW}No depot cache found.${NC}"
        return
    fi
    
    local size=$(du -sh "$OPT_DIR/depots" 2>/dev/null | cut -f1)
    
    if ! confirm "Delete depot cache ($size)?"; then
        echo "Cancelled."
        return
    fi
    
    echo -e "${YELLOW}Stopping service...${NC}"
    systemctl stop $SERVICE 2>/dev/null || true
    
    rm -rf "$OPT_DIR/depots"
    rm -rf "$OPT_DIR/.DepotDownloader"
    
    echo -e "${GREEN}Depot cache cleared.${NC}"
    
    if confirm "Start service now?"; then
        start_service
    fi
}

clear_temp_files() {
    local count=$(ls -1 /tmp/*.zip 2>/dev/null | wc -l || echo "0")
    
    if [ "$count" -eq 0 ]; then
        echo -e "${YELLOW}No temp ZIP files found.${NC}"
        return
    fi
    
    if ! confirm "Delete $count temp ZIP files?"; then
        echo "Cancelled."
        return
    fi
    
    rm -f /tmp/*.zip
    echo -e "${GREEN}Cleared $count temp files.${NC}"
}

full_reset() {
    if ! confirm "This will DELETE ALL DATA including the database. Continue?"; then
        echo "Cancelled."
        return
    fi
    
    if ! confirm "Are you ABSOLUTELY SURE? This cannot be undone!"; then
        echo "Cancelled."
        return
    fi
    
    echo -e "${RED}Performing full reset...${NC}"
    
    echo -e "${YELLOW}Stopping service...${NC}"
    systemctl stop $SERVICE 2>/dev/null || true
    
    echo -e "${YELLOW}Removing database...${NC}"
    rm -f "$DB_FILE"
    
    echo -e "${YELLOW}Removing depot cache...${NC}"
    rm -rf "$OPT_DIR/depots"
    rm -rf "$OPT_DIR/.DepotDownloader"
    
    echo -e "${YELLOW}Removing downloads...${NC}"
    rm -rf "$DATA_DIR/downloads"/*
    
    echo -e "${YELLOW}Removing temp files...${NC}"
    rm -f /tmp/*.zip
    
    # Recreate directories
    mkdir -p "$DATA_DIR/downloads"
    chown -R workshop-archiver:workshop "$DATA_DIR"
    
    echo -e "${GREEN}Full reset complete!${NC}"
    
    if confirm "Start service now?"; then
        start_service
    fi
}

update_binary() {
    echo -e "${CYAN}Update from where?${NC}"
    echo "  1) Local path"
    echo "  2) SCP from remote"
    echo ""
    echo -n "Choice: "
    read -r update_choice
    
    case $update_choice in
        1)
            echo -n "Enter path to new binary: "
            read -r binary_path
            
            if [ ! -f "$binary_path" ]; then
                echo -e "${RED}File not found: $binary_path${NC}"
                return
            fi
            
            echo -e "${YELLOW}Stopping service...${NC}"
            systemctl stop $SERVICE 2>/dev/null || true
            
            echo -e "${YELLOW}Backing up current binary...${NC}"
            cp "$OPT_DIR/GitHubArchiver.Daemon" "$OPT_DIR/GitHubArchiver.Daemon.bak"
            
            echo -e "${YELLOW}Copying new binary...${NC}"
            cp "$binary_path" "$OPT_DIR/GitHubArchiver.Daemon"
            chmod +x "$OPT_DIR/GitHubArchiver.Daemon"
            chown workshop-archiver:workshop "$OPT_DIR/GitHubArchiver.Daemon"
            
            echo -e "${GREEN}Binary updated!${NC}"
            
            if confirm "Start service now?"; then
                start_service
            fi
            ;;
        2)
            echo -n "Enter SCP source (e.g., user@host:/path/to/binary): "
            read -r scp_source
            
            echo -e "${YELLOW}Stopping service...${NC}"
            systemctl stop $SERVICE 2>/dev/null || true
            
            echo -e "${YELLOW}Backing up current binary...${NC}"
            cp "$OPT_DIR/GitHubArchiver.Daemon" "$OPT_DIR/GitHubArchiver.Daemon.bak"
            
            echo -e "${YELLOW}Downloading new binary...${NC}"
            scp "$scp_source" "$OPT_DIR/GitHubArchiver.Daemon"
            chmod +x "$OPT_DIR/GitHubArchiver.Daemon"
            chown workshop-archiver:workshop "$OPT_DIR/GitHubArchiver.Daemon"
            
            echo -e "${GREEN}Binary updated!${NC}"
            
            if confirm "Start service now?"; then
                start_service
            fi
            ;;
        *)
            echo "Cancelled."
            ;;
    esac
}

edit_config() {
    if [ -z "$EDITOR" ]; then
        EDITOR=nano
    fi
    
    echo -e "${CYAN}Opening configuration in $EDITOR...${NC}"
    $EDITOR "$OPT_DIR/appsettings.json"
    
    if confirm "Restart service to apply changes?"; then
        restart_service
    fi
}

backup_database() {
    if [ ! -f "$DB_FILE" ]; then
        echo -e "${YELLOW}No database found.${NC}"
        return
    fi
    
    local backup_file="$DATA_DIR/workshop_backup_$(date +%Y%m%d_%H%M%S).db"
    
    echo -e "${CYAN}Backing up database to ${backup_file}...${NC}"
    cp "$DB_FILE" "$backup_file"
    
    echo -e "${GREEN}Backup created: ${backup_file}${NC}"
    echo -e "Size: $(du -h "$backup_file" | cut -f1)"
}

# Main loop
main() {
    # Check if running as root
    if [ "$EUID" -ne 0 ]; then
        echo -e "${RED}Please run as root${NC}"
        exit 1
    fi
    
    while true; do
        clear
        print_header
        print_status
        print_menu
        
        echo -n "Enter choice: "
        read -r choice
        
        case $choice in
            1) start_service ;;
            2) stop_service ;;
            3) restart_service ;;
            4) enable_service ;;
            5) disable_service ;;
            6) view_logs_live ;;
            7) view_logs_recent ;;
            8) view_logs_errors ;;
            9) view_logs_today ;;
            10) show_recent_archived ;;
            11) show_pending_items ;;
            12) show_failed_downloads ;;
            13) search_items ;;
            14) export_manifest ;;
            15) clear_failed_downloads ;;
            16) clear_depot_cache ;;
            17) clear_temp_files ;;
            18) full_reset ;;
            19) update_binary ;;
            20) edit_config ;;
            21) backup_database ;;
            r|R) continue ;;
            0) echo "Goodbye!"; exit 0 ;;
            *) echo -e "${RED}Invalid option${NC}" ;;
        esac
        
        echo ""
        echo -n "Press Enter to continue..."
        read -r
    done
}

main "$@"
