# /photoadmin -- managing the photo store

The server keeps one source PNG per photograph on disk, under `ModData/photocore/photos/`.
These are the originals every plate, frame, and downloaded client copy is built from, so the mod never deletes them on its own. On a long-running server they pile up -- including photos whose plates were scrapped, lost, or kept in the inventory of players who logged off and never came back.

`/photoadmin` lets an operator find and remove the stale ones. It needs the `controlserver` privilege: you have it in single-player and as a LAN host; on a dedicated server, grant it with `/op` or a role. Every command that deletes anything is a dry run until you add `confirm`.

## How "stale" is judged

As a server's photo source file storage grows over an extended period of time, there's no reliable way for servers to know which photos are constantly being requested by clients (i.e. in the center of a busy town) and which are just taking up space on the disk (i.e. in the inventory of a player who hasn't logged on in months).

A photo nobody has loaded in months is very likely orphaned. A photo with no last-seen time at all & has never been requested since tracking began: usually the strongest sign it's abandoned, though it can also just be brand new. **To avoid wiping a photo someone took minutes ago, anything newer than the grace period (`PhotoDeleteGraceHours` in the config, default 24 hours) is left out of the age-based/count-based
deletes.** Deleting a specific id ignores the grace period.

## Commands

**`/photoadmin stats`**
Totals: how many source photos exist, how much disk they use, and the split between seen, never-seen, and stale index rows (records whose photo file is already gone).

**`/photoadmin audit [count]`**
Lists the least-recently-seen photos (default 20), never-seen first, with each one's last-seen age,
age on disk, and size. Read-only -- nothing is deleted.

**`/photoadmin delete oldest <count> [confirm]`**
Deletes the N least-recently-seen photos.

**`/photoadmin delete olderthan <days> [confirm]`**
Deletes every photo not requested in the last N days. This is the one to reach for when reclaiming
space, e.g. `/photoadmin delete olderthan 90`.

**`/photoadmin delete id <id[,id,...]> [confirm]`**
Deletes specific photos by id, comma-separated with no spaces. Ignores the grace period.
Each entry can be a full id or any unique fragment of one (at least 4 characters), so
`delete id g8x4m2kd` finds `exposure_g8x4m2kd` on its own. A fragment matching more than one
photo is skipped and reported rather than guessing.

**`/photoadmin prune-index [confirm]`**
Drops last-seen records whose photo file is already gone. Tidies the index; frees no image disk.

## Limiting who can develop (the whitelist)

`/photoadmin delete …` reclaims space after the fact. The whitelist tackles the other end by limiting who can add to the store in the first place.

Taking an exposure is local to the player and costs the server nothing. The image only starts living on the server when someone *develops* it -- the first developer pour in a tray. When the develop whitelist is on, only listed players (and operators) can perform this step. Everyone can still expose their own plates as normal, but trusted players or admins gate what lives on the server. It is **off by default**, so nothing changes until turned on.

***Due to current limitations with how exposure data is handled, players can only develop exposures they've personally taken. This means the development whitelist currently effectively limits who can take photographs all together. 
This will soon be changed so that clients can request each other's exposure data, letting all players expose photos and those on the development whitelist act merely as trusted plate finishers.***

**`/photoadmin whitelist status`** -- whether it's on, and how many players are listed.

**`/photoadmin whitelist enable` / `disable`** -- turn it on or off. While on, only listed players and
operators can develop; off, everyone can.

**`/photoadmin whitelist add <player>` / `remove <player>`** -- manage who's allowed. Works on an
online player, or an offline one by their exact last-seen name. `remove` also accepts a raw UID (as
printed by `list`), so you can always drop someone even if their name is gone or changed. Operators
are always allowed and don't need adding.

**`/photoadmin whitelist list`** -- the players currently allowed, with their UIDs.

Changes take effect immediately for everyone online -- no relog needed.

## Notes

- Deleting a photo also removes its derived render files and its last-seen record.
- A deleted photo cannot be recovered from in-game. Any plate or frame still pointing at it renders blank.
- Run a delete without `confirm` first -- it prints exactly what it would remove and how much space that frees.
- The develop whitelist is enforced server-side: a modified client can't bypass it. Disabling it lets
  everyone develop again.
