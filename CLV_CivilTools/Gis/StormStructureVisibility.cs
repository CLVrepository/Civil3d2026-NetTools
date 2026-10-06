using System;
using System.Collections.Generic;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Traverses the host's evaluated entity tree without expanding invisible nodes.
    /// The host owns native entity lookup, transforms, cloning and transactions.
    /// Callback failures intentionally propagate so that conversion can roll back.
    /// </summary>
    internal static class StormStructureVisibility
    {
        // Count the root as level one. Hidden nodes never expand their descendants.
        public const int MaximumDepth = 32;

        public static void VisitVisible<T>(T root, Func<T, bool> isVisible,
            Func<T, IReadOnlyList<T>?> getChildren, Action<T> visitLeaf)
        {
            ArgumentNullException.ThrowIfNull(isVisible);
            ArgumentNullException.ThrowIfNull(getChildren);
            ArgumentNullException.ThrowIfNull(visitLeaf);

            Visit(root, 1);

            void Visit(T node, int depth)
            {
                // Visibility must be checked before querying children: an invisible
                // dynamic-state ancestor suppresses even individually visible leaves.
                if (!isVisible(node))
                    return;
                if (depth > MaximumDepth)
                    throw new InvalidOperationException($"Visible structure geometry exceeds {MaximumDepth} nesting levels.");

                IReadOnlyList<T>? children = getChildren(node);
                if (children == null)
                {
                    visitLeaf(node);
                    return;
                }

                // An empty child list denotes an empty container, never a leaf.
                for (int i = 0; i < children.Count; i++)
                    Visit(children[i], depth + 1);
            }
        }
    }
}
