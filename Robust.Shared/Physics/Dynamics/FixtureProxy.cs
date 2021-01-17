using Robust.Shared.Maths;
using Robust.Shared.Physics.Dynamics;

namespace Robust.Shared.Physics
{
    public class FixtureProxy
    {
        /// <summary>
        ///     Grid-based AABB of this proxy.
        /// </summary>
        public Box2 AABB;

        /// <summary>
        ///     Our parent fixture
        /// </summary>
        public Fixture Fixture;

        /// <summary>
        ///     ID of this proxy in the broadphase dynamictree.
        /// </summary>
        public DynamicTree.Proxy ProxyId = DynamicTree.Proxy.Free;

        public FixtureProxy(Box2 aabb, Fixture fixture)
        {
            AABB = aabb;
            Fixture = fixture;
        }
    }
}
