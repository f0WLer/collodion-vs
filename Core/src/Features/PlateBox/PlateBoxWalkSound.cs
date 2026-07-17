using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Photocore.PlateBox
{
    // Mirrors the base game's armor-footstep mechanic (EntityPlayer.OnFootStep -- its own doc comment
    // says it's "used by the armor system to create armor step sounds," not armor-specific) but keyed
    // off a held plate box instead of worn armor, so a fuller box audibly rattles as its carrier walks.
    // Client predicts its own local player's steps immediately; server drives everyone else's, mirroring
    // ModSystemWearableStats' client/server split so bystanders hear it too, not just the carrier.
    internal static class PlateBoxWalkSoundEvents
    {
        // Not every stored plate audibly clinks on every single step -- real plates shift and settle
        // unevenly. Rolled per plate (not once for the whole step) so which ones sound varies step to
        // step; the stagger delay still advances on a skip so the surviving thuds keep the same cadence
        // instead of bunching up.
        private const float ThudPlayChance = 0.5f;

        // dualCallByPlayer=entity.Player: plays locally with no network round trip, and lets the
        // client-side sound system drop the later echo of the same tagged sound the server broadcasts.
        internal static void ConfigureClientPlateBoxWalkSound(ICoreClientAPI api)
        {
            api.Event.LevelFinalize += () =>
            {
                EntityPlayer entity = api.World.Player.Entity;
                entity.OnFootStep += () => OnFootStep(api.World, entity, entity.Player);
            };
        }

        // dualCallByPlayer=null: broadcasts to everyone in range except the acting player's own client,
        // which already predicted its copy above.
        internal static void ConfigureServerPlateBoxWalkSound(ICoreServerAPI api)
        {
            api.Event.PlayerJoin += byPlayer =>
            {
                EntityPlayer entity = byPlayer.Entity;
                entity.OnFootStep += () => OnFootStep(api.World, entity, null);
            };
        }

        private static void OnFootStep(IWorldAccessor world, EntityPlayer entity, IPlayer? dualCallByPlayer)
        {
            if (!TryGetHeldPlateBoxCount(entity, out int plateCount) || plateCount <= 0) return;

            float volume = entity.Controls.Sneak ? 0.35f : 0.6f;
            int delayMs = 0;

            for (int i = 0; i < plateCount; i++)
            {
                if (world.Rand.NextDouble() < ThudPlayChance)
                {
                    AssetLocation sound = BlockPlateBox._glassThudSounds[world.Rand.Next(BlockPlateBox._glassThudSounds.Length)];
                    float pitch = 0.92f + (float)world.Rand.NextDouble() * 0.16f;
                    PlaySoundAtEntityWithDelay(world, entity, dualCallByPlayer, sound, delayMs, pitch, volume);
                }

                delayMs += BlockPlateBox.PlateThudStaggerMinMs + world.Rand.Next(BlockPlateBox.PlateThudStaggerRangeMs);
            }
        }

        private static bool TryGetHeldPlateBoxCount(EntityPlayer entity, out int plateCount)
        {
            plateCount = 0;

            ItemStack? mainHand = entity.RightHandItemSlot?.Itemstack;
            ItemStack? offhand = entity.LeftHandItemSlot?.Itemstack;

            ItemStack? held = mainHand?.Block is BlockPlateBox ? mainHand
                : offhand?.Block is BlockPlateBox ? offhand
                : null;

            if (held == null) return false;

            plateCount = BlockEntityPlateBox.GetPlateCountFromItemStack(held);
            return true;
        }

        // Entity-tracking counterpart to BlockPlateBox.PlaySoundWithDelay -- that one targets a fixed
        // block position (fine for a box that just landed), this one re-resolves the entity's current
        // position at play time via the AtEntity overload, since the carrier keeps walking during the
        // stagger.
        private static void PlaySoundAtEntityWithDelay(IWorldAccessor world, Entity atEntity, IPlayer? dualCallByPlayer, AssetLocation sound, int delayMs, float pitch, float volume)
        {
            if (delayMs <= 0)
            {
                PlaySoundAtEntityNow(world, atEntity, dualCallByPlayer, sound, pitch, volume);
                return;
            }

            try
            {
                world.Api?.Event?.RegisterCallback(_ => PlaySoundAtEntityNow(world, atEntity, dualCallByPlayer, sound, pitch, volume), delayMs);
            }
            catch (Exception ex)
            {
                Log.Warn(world.Logger, "PlateBoxWalkSound delay scheduling failed, using immediate fallback: {0}", ex.Message);
                PlaySoundAtEntityNow(world, atEntity, dualCallByPlayer, sound, pitch, volume);
            }
        }

        private static void PlaySoundAtEntityNow(IWorldAccessor world, Entity atEntity, IPlayer? dualCallByPlayer, AssetLocation sound, float pitch, float volume)
        {
            try
            {
                world.PlaySoundAt(sound, atEntity, dualCallByPlayer, pitch, 16f, volume);
            }
            catch (Exception ex)
            {
                Log.Debug(world.Logger, "PlateBoxWalkSound play failed: {0}", ex.Message);
            }
        }
    }
}
