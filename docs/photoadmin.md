# /photoadmin — managing the photo store

The server keeps one source PNG per photograph on disk, under `ModData/photochemistry/photos/`.
These are the originals every plate, frame, and downloaded client copy is built from, so the mod
never deletes them on its own. On a long-running server they pile up — including photos whose plates
were scrapped, lost, or carried off by players who never came back.

`/photoadmin` lets an operator find and remove the stale ones. It needs the `controlserver`
privilege: you have it in single-player and as a LAN host; on a dedicated server, grant it with `/op`
or a role. Every command that deletes anything is a dry run until you add `confirm`.

## How "stale" is judged

The server has no reliable way to know which photos are still in use — plates sit in inventories and
chests that may not even be loaded. Instead the mod records the last time any client requested each
photo. A photo nobody has loaded in months is very likely orphaned. A photo with no last-seen time at
all has never been requested since tracking began: usually the strongest sign it's abandoned, though
it can also just be brand new.

To avoid wiping a photo someone took minutes ago, anything newer than the grace period
(`PhotoDeleteGraceHours` in the config, default 24 hours) is left out of the age- and count-based
deletes. Deleting a specific id ignores the grace period — if you name it, you get it.

## Commands

**`/photoadmin stats`**
Totals: how many source photos exist, how much disk they use, and the split between seen, never-seen,
and stale index rows (records whose photo file is already gone).

**`/photoadmin audit [count]`**
Lists the least-recently-seen photos (default 20), never-seen first, with each one's last-seen age,
age on disk, and size. Read-only — nothing is deleted.

**`/photoadmin delete oldest <count> [confirm]`**
Deletes the N least-recently-seen photos.

**`/photoadmin delete olderthan <days> [confirm]`**
Deletes every photo not requested in the last N days. This is the one to reach for when reclaiming
space, e.g. `/photoadmin delete olderthan 90`.

**`/photoadmin delete id <id[,id,...]> [confirm]`**
Deletes specific photos by id, comma-separated with no spaces. Ignores the grace period.

**`/photoadmin prune-index [confirm]`**
Drops last-seen records whose photo file is already gone. Tidies the index; frees no image disk.

## Limiting who can develop (the whitelist)

`/photoadmin delete …` reclaims space after the fact. The whitelist tackles the other end: it limits
who can add to the store in the first place.

Taking an exposure is local to the player and costs the server nothing. The image only starts living
on the server when someone *develops* it — the first developer pour in a tray, or a field-camera
capture finalising. When the develop whitelist is on, only listed players (and operators) can perform
that step. Everyone can still expose plates as normal; a blocked player who tries to develop is told
so, and keeps their exposure to hand off to someone who can. It is **off by default**, so nothing
changes until you turn it on.

**`/photoadmin whitelist status`** — whether it's on, and how many players are listed.

**`/photoadmin whitelist enable` / `disable`** — turn it on or off. While on, only listed players and
operators can develop; off, everyone can.

**`/photoadmin whitelist add <player>` / `remove <player>`** — manage who's allowed. Works on an
online player, or an offline one by their exact last-seen name. `remove` also accepts a raw UID (as
printed by `list`), so you can always drop someone even if their name is gone or changed. Operators
are always allowed and don't need adding.

**`/photoadmin whitelist list`** — the players currently allowed, with their UIDs.

Changes take effect immediately for everyone online — no relog needed.

## Notes

- Deleting a photo also removes its derived render files and its last-seen record.
- A deleted photo cannot be recovered from in-game. Any plate or frame still pointing at it renders blank.
- Run a delete without `confirm` first — it prints exactly what it would remove and how much space that frees.
- The develop whitelist is enforced server-side: a modified client can't bypass it. Disabling it lets
  everyone develop again.
