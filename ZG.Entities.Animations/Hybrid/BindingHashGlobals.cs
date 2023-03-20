namespace Unity.Animation.Hybrid
{
    /// <summary>
    /// Defines common hashing strategies for animation bindings.
    /// </summary>
    public static class BindingHashGlobals
    {
        internal static string BuildPath(string path, string property)
        {
            bool nullPath = string.IsNullOrEmpty(path);
            bool nullProperty = string.IsNullOrEmpty(property);

            if (nullPath && nullProperty)
                return string.Empty;
            if (nullPath)
                return property;
            if (nullProperty)
                return path;

            return path + "/" + property;
        }

        /// <summary>
        /// System wide default hash generator for animation bindings. This can be overriden to
        /// a custom definition when needed via a <see cref="UnityEngine.RuntimeInitializeOnLoadMethodAttribute"/>
        /// </summary>
        public static BindingHashGenerator DefaultHashGenerator = new BindingHashGenerator
        {
            TransformBindingHashFunction = TransformBindingHashFullPath,
            GenericBindingHashFunction = GenericBindingHashFullPath
        };

        /// <summary>
        /// Uses the full TransformBindingID path to generate a unique hash.
        /// </summary>
        /// <param name="id">TransformBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        public static uint TransformBindingHashFullPath(TransformBindingID id) => StringHash.Hash(id.ID);

        /// <summary>
        /// Uses the transform name of the TransformBindingID to generate a unique hash.
        /// </summary>
        /// <param name="id">TransformBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        public static uint TransformBindingHashName(TransformBindingID id) => StringHash.Hash(id.Name);

        /// <summary>
        /// Uses the full GenericBindingID path to generate a unique hash.
        /// </summary>
        /// <param name="id">GenericBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        public static uint GenericBindingHashFullPath(GenericBindingID id)
        {
            // TODO: This function should return the following instead:
            // StringHash.Hash(id.ID);
            // However a few workflows are currently missing to properly support the ComponentType via tooling.
            // In the meantime, we'll stick to the current hashing strategies for generic bindings.
            
        
            return StringHash.Hash(BuildPath(id.Path, id.AttributeName));
        }

        /// <summary>
        /// Uses the attribute name of the GenericBindingID path to generate a unique hash.
        /// </summary>
        /// <param name="id">GenericBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        public static uint GenericBindingHashName(GenericBindingID id)
        {
            // TODO: This function should return the following instead:
            // StringHash.Hash($"{id.AttributeName}:{id.ComponentType?.AssemblyQualifiedName}");
            // However a few workflows are missing currently to properly support the ComponentType via tooling.
            // In the meantime, we'll stick to the current hashing strategies for generic bindings.

            return StringHash.Hash(id.AttributeName);
        }
    }
}
