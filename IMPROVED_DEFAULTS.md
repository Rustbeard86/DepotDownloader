# Improved Defaults Summary

## Changes Implemented

### 1. Resume is Now Default Behavior ?

**Previous Behavior:**
- Users had to specify `-resume` flag to enable resume functionality
- Downloads would start from scratch if interrupted

**New Behavior:**
- Resume is **enabled by default**
- Downloads automatically continue from where they left off if interrupted
- Use `-no-resume` flag to disable if needed (rare case)

**Rationale:**
- Users expect downloads to resume automatically (modern UX standard)
- Reduces wasted bandwidth and time
- Rare that someone would want to restart from scratch

**Examples:**
```powershell
# Resume happens automatically (default)
./DepotDownloader -app 730 -dir "C:\Games\CS2"
# If interrupted, run the same command to continue

# Disable resume (rare)
./DepotDownloader -app 730 -dir "C:\Games\CS2" -no-resume
```

```csharp
// Library - resume is default
var options = new DepotDownloadOptions
{
    AppId = 730,
    InstallDirectory = @"C:\Games\CS2",
    Resume = true  // Default value
};

// Disable if needed
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithResume(false)
    .Build();
```

---

### 2. `-max-speed 0` Means Unlimited ?

**Previous Behavior:**
- `-max-speed 0` would set speed limit to 0 bytes/sec (effectively blocking downloads)
- No way to explicitly set unlimited via CLI

**New Behavior:**
- `-max-speed 0` or negative values mean **unlimited** (no throttling)
- Default behavior (no `-max-speed` flag) is also unlimited
- Positive values set the limit in MB/s

**Rationale:**
- Intuitive: 0 = no limit
- Matches common convention (e.g., bandwidth limiters, QoS tools)
- Allows users to explicitly request unlimited speed

**Examples:**
```powershell
# Limit to 10 MB/s
./DepotDownloader -app 730 -max-speed 10

# Unlimited (explicit)
./DepotDownloader -app 730 -max-speed 0

# Unlimited (implicit, no flag)
./DepotDownloader -app 730
```

```csharp
// Limit to 10 MB/s
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithMaxSpeedMbps(10)
    .Build();

// Unlimited (explicit)
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithMaxSpeedMbps(0)  // or negative value
    .Build();

// Unlimited (implicit, don't call WithMaxSpeed)
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .Build();
```

---

### 3. Unlimited Retries NOT Supported ? (Intentional)

**Why Not?**
- Could hang forever on permanent failures (e.g., 404, access denied, invalid manifest)
- Hides real errors from users
- Better to fail fast and report the issue

**Current Options:**
- Default: 5 retries with exponential backoff
- `-retries 0`: Disable retries completely
- `-retries <n>`: Set specific retry count (1-100 reasonable range)

**Predefined Policies:**
```csharp
RetryPolicy.Default      // 5 retries, exponential backoff
RetryPolicy.Aggressive   // 10 retries, longer delays
RetryPolicy.None         // No retries

// Custom
RetryPolicy.Create(maxRetries: 10)
```

---

### 4. `-get-manifest` Requires `-depot` ? (Intentional)

**Why Not Auto-Select?**
- Apps often have 10+ depots (Windows, Linux, Mac, DLCs, language packs)
- Unclear which depot the user wants
- Better to be explicit than guess wrong

**Current Behavior:**
```powershell
# Error: -depot must be specified
./DepotDownloader -app 730 -get-manifest

# Correct usage
./DepotDownloader -app 730 -depot 731 -get-manifest
```

**Suggested Workflow:**
```powershell
# Step 1: List available depots
./DepotDownloader -app 730 -list-depots

# Step 2: Get manifest for specific depot
./DepotDownloader -app 730 -depot 731 -get-manifest
```

---

## Documentation Updates

### CLI Help Text
- ? `-max-speed` - Added "(0 for unlimited)"
- ? `-resume` ? `-no-resume` - Now shows it disables default behavior
- ? All parameter descriptions updated

### README.md
- ? CLI Parameters Reference table updated
- ? Download Options examples updated
- ? Library usage examples updated
- ? Builder method table updated
- ? FAQ entries updated
- ? Full options reference updated

### FEATURE_COVERAGE_SUMMARY.md
- ? Resume support entry updated to reflect default behavior

---

## Impact Assessment

### Breaking Changes
**None** - These are improvements to defaults, not breaking changes:
- Old scripts with `-resume` still work (flag is accepted but redundant)
- Old scripts without `-resume` now get better behavior automatically
- `-max-speed` with positive values works exactly the same
- Library code with explicit `Resume = false` still works

### Benefits
1. **Better UX** - Downloads resume automatically (expected behavior)
2. **Less Bandwidth Waste** - Interrupted downloads don't restart from scratch
3. **Intuitive Speed Control** - `0` = unlimited matches user expectations
4. **Explicit Control** - Users can still disable resume or set unlimited speed explicitly

### Migration Guide
**For CLI Users:**
- No changes needed! Your existing scripts work better now.
- Remove `-resume` flags (now redundant, but harmless)
- Use `-no-resume` if you specifically want to restart from scratch

**For Library Users:**
```csharp
// Old code (still works, but Resume = true is now default)
var options = new DepotDownloadOptions
{
    AppId = 730,
    Resume = true  // No longer needed
};

// New recommended code (leverage defaults)
var options = new DepotDownloadOptions
{
    AppId = 730
    // Resume = true is implicit
};

// Disable resume if needed
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithResume(false)
    .Build();
```

---

## Testing Recommendations

1. **Resume Functionality**
   - ? Start download, interrupt (Ctrl+C), restart
   - ? Verify it continues from last checkpoint
   - ? Verify `-no-resume` restarts from scratch

2. **Speed Limiting**
   - ? `-max-speed 10` limits to ~10 MB/s
   - ? `-max-speed 0` has no limit
   - ? No `-max-speed` flag has no limit

3. **Backward Compatibility**
   - ? Scripts with `-resume` still work
   - ? Library code with explicit `Resume = true` still works
   - ? Existing config files still work

---

## Summary

| Change | Status | Impact | Breaking? |
|--------|--------|--------|-----------|
| Resume default | ? Implemented | Better UX, less bandwidth waste | No |
| `-max-speed 0` = unlimited | ? Implemented | Intuitive control | No |
| No unlimited retries | ? Intentional | Prevents infinite loops | N/A |
| Require `-depot` for `-get-manifest` | ? Intentional | Explicit over implicit | N/A |

All changes improve the user experience without breaking existing functionality.
