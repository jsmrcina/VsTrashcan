using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using VsTrashcan.Pickup;
using Xunit;

namespace VsTrashcan.Tests
{
    public class PickupHookTests
    {
        private class OtherBehavior : EntityBehavior
        {
            public OtherBehavior(Entity entity) : base(entity) { }
            public override string PropertyName() => "other";
        }

        private class SomeoneElsesCollect : EntityBehaviorCollectEntities
        {
            public SomeoneElsesCollect(Entity entity) : base(entity) { }
        }

        private class TestEntity : Entity
        {
            public TestEntity(TestWorld world)
            {
                World = world.World.Object;
                Api = world.Api.Object;
                Properties = new EntityProperties { Server = new EntityServerProperties(new JsonObject[0], new Dictionary<string, JsonObject>()) };
                CacheServerBehaviors();
            }

            public List<EntityBehavior> Behaviors => Properties.Server.Behaviors;
        }

        private readonly TestWorld world = new TestWorld();

        private TestEntity EntityWith(System.Func<Entity, EntityBehavior>[] behaviors)
        {
            var e = new TestEntity(world);
            foreach (var make in behaviors) e.Behaviors.Add(make(e));
            e.CacheServerBehaviors();
            return e;
        }

        [Fact]
        public void ReplacesTheVanillaCollectBehaviorInPlace()
        {
            var e = EntityWith(new System.Func<Entity, EntityBehavior>[] { x => new OtherBehavior(x), x => new EntityBehaviorCollectEntities(x), x => new OtherBehavior(x) });

            Assert.True(EntityBehaviorTrashcanCollect.Install(e));

            Assert.Equal(3, e.Behaviors.Count);
            Assert.IsType<OtherBehavior>(e.Behaviors[0]);
            Assert.IsType<EntityBehaviorTrashcanCollect>(e.Behaviors[1]);
            Assert.IsType<OtherBehavior>(e.Behaviors[2]);
            Assert.Contains(e.ServerBehaviorsMainThread, b => b is EntityBehaviorTrashcanCollect); // the tick cache was refreshed
            Assert.DoesNotContain(e.ServerBehaviorsMainThread, b => b.GetType() == typeof(EntityBehaviorCollectEntities));
        }

        [Fact]
        public void InstallingTwiceChangesNothing()
        {
            var e = EntityWith(new System.Func<Entity, EntityBehavior>[] { x => new EntityBehaviorCollectEntities(x) });
            EntityBehaviorTrashcanCollect.Install(e);
            var installed = e.Behaviors[0];

            Assert.True(EntityBehaviorTrashcanCollect.Install(e));

            Assert.Same(installed, Assert.Single(e.Behaviors));
        }

        [Fact]
        public void AnEntityWithoutCollectingIsLeftAlone()
        {
            var e = EntityWith(new System.Func<Entity, EntityBehavior>[] { x => new OtherBehavior(x) });

            Assert.False(EntityBehaviorTrashcanCollect.Install(e));
            Assert.IsType<OtherBehavior>(Assert.Single(e.Behaviors));
        }

        [Fact]
        public void AnotherModsCollectBehaviorIsNotReplaced()
        {
            var e = EntityWith(new System.Func<Entity, EntityBehavior>[] { x => new SomeoneElsesCollect(x) });

            Assert.False(EntityBehaviorTrashcanCollect.Install(e));
            Assert.IsType<SomeoneElsesCollect>(Assert.Single(e.Behaviors));
        }

        [Fact]
        public void TheHookKeepsTheVanillaBehaviorsName()
        {
            var e = new TestEntity(world);
            Assert.Equal("collectitems", new EntityBehaviorTrashcanCollect(e).PropertyName());
        }
    }
}
