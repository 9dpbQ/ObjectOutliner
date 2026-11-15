using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal static class ObjectOrderStore
    {
        private const string OrderKey = "ObjectManager.Outliner.Order.v1";

        public static bool TryGetOrder(RhinoObject rhinoObject, out int order)
        {
            order = 0;
            return rhinoObject != null &&
                   rhinoObject.Attributes.UserDictionary.TryGetInteger(OrderKey, out order);
        }

        public static bool TryGetOrder(ObjectAttributes attributes, out int order)
        {
            order = 0;
            return attributes != null &&
                   attributes.UserDictionary.TryGetInteger(OrderKey, out order);
        }

        public static void SetOrder(ObjectAttributes attributes, int order)
        {
            attributes.UserDictionary[OrderKey] = order;
        }
    }
}
