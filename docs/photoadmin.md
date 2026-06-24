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

## Notes

- Deleting a photo also removes its derived render files and its last-seen record.
- A deleted photo cannot be recovered from in-game. Any plate or frame still pointing at it renders blank.
- Run a delete without `confirm` first — it prints exactly what it would remove and how much space that frees.
