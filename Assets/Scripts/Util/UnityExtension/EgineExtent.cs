using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace UnityExtension
{
    public static class EngineExtent
    {
        public static GenericComponent GetOrAddComponent<GenericComponent>(this GameObject gameObject) where GenericComponent : Component
        {
            GenericComponent component = gameObject.GetComponent<GenericComponent>();
            if (component == null)
                return gameObject.AddComponent<GenericComponent>();
            else
                return component;
        }

        public static void GetOrAddComponents<GenericComponent>(this GameObject gameObject, out GenericComponent component) where GenericComponent : Component
        {
            component = gameObject.GetComponent<GenericComponent>() ?? gameObject.AddComponent<GenericComponent>();
        }

        public static void GetOrAddComponents<GenericComponent>(this GameObject gameObject, out GenericComponent component0, out GenericComponent component1) where GenericComponent : Component
        {
            GenericComponent[] components = gameObject.GetComponents<GenericComponent>();

            if (components.Length > 0) component0 = components[0];
            else component0 = gameObject.AddComponent<GenericComponent>();

            if (components.Length > 1) component1 = components[1];
            else component1 = gameObject.AddComponent<GenericComponent>();
        }

        public static void GetOrAddComponents<GenericComponent>(this GameObject gameObject, out GenericComponent component0, out GenericComponent component1, out GenericComponent component2) where GenericComponent : Component
        {
            GenericComponent[] components = gameObject.GetComponents<GenericComponent>();

            if (components.Length > 0) component0 = components[0];
            else component0 = gameObject.AddComponent<GenericComponent>();

            if (components.Length > 1) component1 = components[1];
            else component1 = gameObject.AddComponent<GenericComponent>();

            if (components.Length > 2) component2 = components[2];
            else component2 = gameObject.AddComponent<GenericComponent>();
        }

        public static GenericComponent GetRequiredComponent<GenericComponent>(this GameObject gameObject) where GenericComponent : Component
        {
            GenericComponent component = gameObject.GetComponent<GenericComponent>();
            if (component == null)
            {
                throw new MissingComponentException("Missing required component: " + typeof(GenericComponent).ToString());
            }

            return component;
        }

        public static GenericComponent AddExternalComponent<GenericComponent>(this GameObject gameObject, string containerName = "ComponentContainer", bool asChildren = true) where GenericComponent : Component
        {
            GameObject componentContainer = new GameObject(containerName, typeof(GenericComponent));
            if (asChildren)
                componentContainer.transform.parent = gameObject.transform;

            return componentContainer.GetComponent<GenericComponent>();
        }
    }


}

