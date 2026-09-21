# Angband3D — Universal Save Game System & Portability Guide

## Save Format Specification

Angband 4.2.6 saves games as binary block archives. All valid save files adhere to the **`SaveVNLA`** layout:

| Offset | Length | Type | Description |
|---|---|---|---|
| `0x00 - 0x07` | 8 bytes | ASCII | Magic string: `SaveVNLA` |
| `0x08 - 0x17` | 16 bytes | ASCII | Block name: `"description\0"` |
| `0x18 - 0x1B` | 4 bytes | uint32_le | Block version number |
| `0x1C - 0x1F` | 4 bytes | uint32_le | Size of description payload |
| `0x20 - 0x23` | 4 bytes | uint32_le | Block checksum |
| `0x24 - ...` | Variable | UTF-8 | Null-terminated string: `<CharacterName>, Level <N> <Race> <Class>` |

### Security & Integrity Validation
Before loading or accepting an uploaded file, both the client and server check:
1. File size $\ge 36$ bytes.
2. First 8 bytes equal `SaveVNLA`.
3. First block is `description` with a non-zero, valid size.

Any file failing this test is rejected with `400 Bad Request` or an in-game warning to prevent memory corruption.

---

## Save Game Features in Angband3D

### 1. In-Game Save Game Manager
Accessible from the Main Menu under:
`Save Game Manager (Backup / Export / Import)`

- **Backup All Saves to Local Archive**:
  Creates a timestamped `.sav` snapshot in `lib/save/backups/<name>_YYYYMMDD_HHMMSS.sav`.
- **Export Save to Downloads Folder**:
  Copies the character's save file directly to the OS `Downloads` folder as `<name>.sav` for sharing or manual cloud backup.
- **Import Save from Backup Archive**:
  Scans the archive directory, verifies the `SaveVNLA` header, and restores the save to the active directory.
- **Upload Local Save to Cloud Realm**:
  Transfers the selected local character directly to the connected cloud server via HTTP POST `/api/saves/upload`.
- **Download Saves from Cloud Realm**:
  Synchronizes all characters hosted in the cloud container down to your local `lib/save/` directory.

### 2. Permadeath Recovery & Snapshots
Angband permanently removes save files upon character death.
- In `OpenPauseMenu()`, the action **`Backup Active Character Save (.sav)`** creates a snapshot without leaving the dungeon.
- If your character perishes, you can restore from your backup archive using `Save Game Manager -> Import Save from Backup Archive`.

---

## REST Save Management API (Cloud Server)

The cloud container daemon provides full REST endpoints for save operations:

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/saves` | Returns JSON array of all saved characters with metadata (level, class, depth, timestamp). |
| `GET` | `/api/saves/:name` | Streams raw binary `.sav` file. |
| `POST` | `/api/saves/upload` | Uploads binary `.sav` file. Validates `SaveVNLA` header. Sets filename via `X-Character-Name` header or description parsing. |
| `DELETE` | `/api/saves/:name` | Deletes a save file. |
