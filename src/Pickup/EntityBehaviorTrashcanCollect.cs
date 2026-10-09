using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VsTrashcan.Pickup
{
    /*
        Takes the place of the game's "collectitems" behavior on each player, so auto-trash happens as an item is picked up
        and never touches what is already in the inventory. 1.0.x instead swept the whole inventory every 5 seconds, so a
        filter destroyed items the player already owned.

        Why this hook:
            - EntityPlayer.TryGiveItemStack goes straight to the inventory manager and skips entity behaviors, so a
              TryGiveItemStack behavior would never see a player's pickups
            - the class registry can't be overridden ("collectitems" is already registered; a second registration
              throws), so the instance is swapped on the player's entity instead (see Install)
    */
    public class EntityBehaviorTrashcanCollect : EntityBehaviorCollectEntities
    {
        private VsTrashcan modSystem;

        public EntityBehaviorTrashcanCollect(Entity entity) : base(entity)
        {
        }

        /*
            Swaps the vanilla collect behavior on this entity for ours, at the same position in the behavior list.
            Returns false if the entity has no vanilla collect behavior (or another mod replaced it with its own subclass),
            in which case nothing is changed. Safe to call more than once.
        */
        public static bool Install(Entity entity)
        {
            var behaviors = entity.SidedProperties?.Behaviors;
            if (behaviors == null)
            {
                return false;
            }

            for (int i = 0; i < behaviors.Count; i++)
            {
                if (behaviors[i] is EntityBehaviorTrashcanCollect)
                {
                    return true;
                }

                if (behaviors[i].GetType() == typeof(EntityBehaviorCollectEntities))
                {
                    behaviors[i] = new EntityBehaviorTrashcanCollect(entity);
                    if (entity.World.Side == EnumAppSide.Server)
                    {
                        entity.CacheServerBehaviors();
                    }
                    return true;
                }
            }
            return false;
        }

        public override bool OnFoundCollectible(Entity foundEntity)
        {
            if (entity.World.Side == EnumAppSide.Server && entity is EntityPlayer player && foundEntity is EntityItem item && item.Itemstack != null)
            {
                modSystem ??= entity.Api.ModLoader.GetModSystem<VsTrashcan>();
                if (modSystem?.Server?.ShouldAutoTrash(player.PlayerUID, item.Itemstack) == true)
                {
                    foundEntity.Die(EnumDespawnReason.PickedUp);
                    return true;
                }
            }

            return base.OnFoundCollectible(foundEntity);
        }
    }
}
