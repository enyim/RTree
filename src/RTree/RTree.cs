using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Enyim.Collections
{
	// port of https://github.com/mourner/rbush/
	public class RTree<T>
	{
		private static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

		// per-bucket
		private readonly int maxEntries;
		private readonly int minEntries;

		private RTreeNode<T> root;

		public RTree(int maxEntries = 9)
		{
			this.maxEntries = Math.Max(4, maxEntries);
			this.minEntries = (int)Math.Max(2, Math.Ceiling((double)this.maxEntries * 0.4));

			Clear();
		}

		public void Load(IEnumerable<RTreeNode<T>> nnnn)
		{
			var nodes = nnnn.ToList();

			if (nodes.Count < minEntries)
			{
				foreach (var n in nodes) Insert(n);

				return;
			}

			// recursively build the tree with the given data from stratch using OMT algorithm
			var node = BuildOneLevel(nodes, 0, 0);

			if (root.Children.Count == 0)
			{
				// save as is if tree is empty
				root = node;

			}
			else if (root.Height == node.Height)
			{
				// split root if trees have the same height
				SplitRoot(root, node);

			}
			else
			{
				if (root.Height < node.Height)
				{
					// swap trees if inserted one is bigger
					var tmpNode = root;
					root = node;
					node = tmpNode;
				}

				// insert the small tree into the large tree at appropriate level
				Insert(node, root.Height - node.Height - 1);
			}
		}

		private RTreeNode<T> BuildOneLevel(List<RTreeNode<T>> items, int level, int height)
		{
			RTreeNode<T> node;
			var N = items.Count;
			var M = maxEntries;

			if (N <= M)
			{
				node = new RTreeNode<T> { IsLeaf = true, Height = 1 };
				node.Children.AddRange(items);
			}
			else
			{
				if (level == 0)
				{
					// target height of the bulk-loaded tree
					height = (int)Math.Ceiling(Math.Log(N) / Math.Log(M));

					// target number of root entries to maximize storage utilization
					M = (int)Math.Ceiling((double)N / Math.Pow(M, height - 1));

					items.Sort(CompareNodesByMinX);
				}

				node = new RTreeNode<T> { Height = height };

				var N1 = (int)(Math.Ceiling((double)N / M) * Math.Ceiling(Math.Sqrt(M)));
				var N2 = (int)Math.Ceiling((double)N / M);

				var compare = level % 2 == 1
								? new Comparison<RTreeNode<T>>(CompareNodesByMinX)
								: new Comparison<RTreeNode<T>>(CompareNodesByMinY);

				// split the items into M mostly square tiles
				for (var i = 0; i < N; i += N1)
				{
					var slice = items.GetRange(i, N1);
					slice.Sort(compare);

					for (var j = 0; j < slice.Count; j += N2)
					{
						// pack each entry recursively
						var childNode = BuildOneLevel(slice.GetRange(j, N2), level + 1, height - 1);
						node.Children.Add(childNode);
					}
				}
			}

			RefreshEnvelope(node);

			return node;
		}

		public IEnumerable<RTreeNode<T>> Search(Envelope envelope)
		{
			var node = root;

			if (!envelope.Intersects(node.Envelope)) return Enumerable.Empty<RTreeNode<T>>();

			var retval = new List<RTreeNode<T>>();
			var nodesToSearch = new Stack<RTreeNode<T>>();

			while (node != null)
			{
				for (var i = 0; i < node.Children.Count; i++)
				{
					var child = node.Children[i];
					var childEnvelope = child.Envelope;

					if (envelope.Intersects(childEnvelope))
					{
						if (node.IsLeaf) retval.Add(child);
						else if (envelope.Contains(childEnvelope)) Collect(child, retval);
						else nodesToSearch.Push(child);
					}
				}

				node = nodesToSearch.TryPop();
			}

			return retval;
		}

		private static void Collect(RTreeNode<T> node, List<RTreeNode<T>> result)
		{
			var nodesToSearch = new Stack<RTreeNode<T>>();
			while (node != null)
			{
				if (node.IsLeaf) result.AddRange(node.Children);
				else
				{
					foreach (var n in node.Children)
						nodesToSearch.Push(n);
				}

				node = nodesToSearch.TryPop();
			}
		}

		public void Clear()
		{
			root = new RTreeNode<T> { IsLeaf = true, Height = 1 };
		}

		public void Insert(RTreeNode<T> item)
		{
			Insert(item, root.Height - 1);
		}

		public void Insert(T data, Envelope bounds)
		{
			Insert(new RTreeNode<T>(data, bounds));
		}

		private void Insert(RTreeNode<T> item, int level)
		{
			var envelope = item.Envelope;
			var insertPath = new List<RTreeNode<T>>();

			// find the best node for accommodating the item, saving all nodes along the path too
			var node = ChooseSubtree(envelope, root, level, insertPath);

			// put the item into the node
			node.Children.Add(item);
			node.Envelope.Extend(envelope);

			// split on node overflow; propagate upwards if necessary
			while (level >= 0)
			{
				if (insertPath[level].Children.Count <= maxEntries) break;

				Split(insertPath, level);
				level--;
			}

			// adjust bboxes along the insertion path
			AdjutsParentBounds(envelope, insertPath, level);
		}

		private static int CombinedArea(Envelope what, Envelope with)
		{
			var minX1 = Math.Max(what.X1, with.X1);
			var minY1 = Math.Max(what.Y1, with.Y1);
			var maxX2 = Math.Min(what.X2, with.X2);
			var maxY2 = Math.Min(what.Y2, with.Y2);

			return (maxX2 - minX1) * (maxY2 - minY1);
		}

		private static int IntersectionArea(Envelope what, Envelope with)
		{
			var minX = Math.Max(what.X1, with.X1);
			var minY = Math.Max(what.Y1, with.Y1);
			var maxX = Math.Min(what.X2, with.X2);
			var maxY = Math.Min(what.Y2, with.Y2);

			return Math.Max(0, maxX - minX) * Math.Max(0, maxY - minY);
		}

		private RTreeNode<T> ChooseSubtree(Envelope bbox, RTreeNode<T> node, int level, List<RTreeNode<T>> path)
		{
			while (true)
			{
				path.Add(node);

				if (node.IsLeaf || path.Count - 1 == level) break;

				var minArea = Int32.MaxValue;
				var minEnlargement = Int32.MaxValue;

				RTreeNode<T> targetNode = null;

				for (var i = 0; i < node.Children.Count; i++)
				{
					var child = node.Children[i];
					var area = child.Envelope.Area;
					var enlargement = CombinedArea(bbox, child.Envelope) - area;

					// choose entry with the least area enlargement
					if (enlargement < minEnlargement)
					{
						minEnlargement = enlargement;
						minArea = area < minArea ? area : minArea;
						targetNode = child;

					}
					else if (enlargement == minEnlargement)
					{
						// otherwise choose one with the smallest area
						if (area < minArea)
						{
							minArea = area;
							targetNode = child;
						}
					}
				}

				Debug.Assert(targetNode != null);
				node = targetNode;
			}

			return node;
		}

		// split overflowed node into two
		private void Split(List<RTreeNode<T>> insertPath, int level)
		{
			var node = insertPath[level];
			var totalCount = node.Children.Count;

			ChooseSplitAxis(node, minEntries, totalCount);

			var newNode = new RTreeNode<T> { Height = node.Height };
			var splitIndex = ChooseSplitIndex(node, minEntries, totalCount);

			newNode.Children.AddRange(node.Children.GetRange(splitIndex, node.Children.Count - splitIndex));
			node.Children.RemoveRange(splitIndex, node.Children.Count - splitIndex);

			if (node.IsLeaf) newNode.IsLeaf = true;

			RefreshEnvelope(node);
			RefreshEnvelope(newNode);

			if (level > 0) insertPath[level - 1].Children.Add(newNode);
			else SplitRoot(node, newNode);
		}

		private void SplitRoot(RTreeNode<T> node, RTreeNode<T> newNode)
		{
			// split root node
			root = new RTreeNode<T>
			{
				Children = { node, newNode },
				Height = node.Height + 1
			};

			RefreshEnvelope(root);
		}

		private int ChooseSplitIndex(RTreeNode<T> node, int minEntries, int totalCount)
		{
			var minOverlap = Int32.MaxValue;
			var minArea = Int32.MaxValue;
			int index = 0;

			for (var i = minEntries; i <= totalCount - minEntries; i++)
			{
				var bbox1 = SumChildBounds(node, 0, i);
				var bbox2 = SumChildBounds(node, i, totalCount);

				var overlap = IntersectionArea(bbox1, bbox2);
				var area = bbox1.Area + bbox2.Area;

				// choose distribution with minimum overlap
				if (overlap < minOverlap)
				{
					minOverlap = overlap;
					index = i;

					minArea = area < minArea ? area : minArea;
				}
				else if (overlap == minOverlap)
				{
					// otherwise choose distribution with minimum area
					if (area < minArea)
					{
						minArea = area;
						index = i;
					}
				}
			}

			return index;
		}

		public void Remove(T item, Envelope envelope)
		{
			var node = root;
			var itemEnvelope = envelope;

			var path = new Stack<RTreeNode<T>>();
			var indexes = new Stack<int>();

			var i = 0;
			var goingUp = false;
			RTreeNode<T> parent = null;

			// depth-first iterative tree traversal
			while (node != null || path.Count > 0)
			{
				if (node == null)
				{
					// go up
					node = path.TryPop();
					parent = path.TryPeek();
					i = indexes.TryPop();

					goingUp = true;
				}

				if (node != null && node.IsLeaf)
				{
					// check current node
					var index = node.Children.FindIndex(n => Comparer.Equals(item, n.Data));

					if (index != -1)
					{
						// item found, remove the item and condense tree upwards
						node.Children.RemoveAt(index);
						path.Push(node);
						CondenseNodes(path.ToArray());

						return;
					}
				}

				if (!goingUp && !node.IsLeaf && node.Envelope.Contains(itemEnvelope))
				{
					// go down
					path.Push(node);
					indexes.Push(i);
					i = 0;
					parent = node;
					node = node.Children[0];

				}
				else if (parent != null)
				{
					// go right
					i++;
					node = parent.Children[i];
					goingUp = false;

				}
				else node = null; // nothing found
			}
		}

		private void CondenseNodes(IList<RTreeNode<T>> path)
		{
			// go through the path, removing empty nodes and updating bboxes
			for (var i = path.Count - 1; i >= 0; i--)
			{
				if (path[i].Children.Count == 0)
				{
					if (i == 0)
					{
						Clear();
					}
					else
					{
						var siblings = path[i - 1].Children;
						siblings.Remove(path[i]);
					}
				}
				else
				{
					RefreshEnvelope(path[i]);
				}
			}
		}

		// calculate node's bbox from bboxes of its children
		private static void RefreshEnvelope(RTreeNode<T> node)
		{
			node.Envelope = SumChildBounds(node, 0, node.Children.Count);
		}

		private static Envelope SumChildBounds(RTreeNode<T> node, int startIndex, int endIndex)
		{
			var retval = new Envelope();

			for (var i = startIndex; i < endIndex; i++)
			{
				retval.Extend(node.Children[i].Envelope);
			}

			return retval;
		}

		private static void AdjutsParentBounds(Envelope bbox, List<RTreeNode<T>> path, int level)
		{
			// adjust bboxes along the given tree path
			for (var i = level; i >= 0; i--)
			{
				path[i].Envelope.Extend(bbox);
			}
		}

		// sorts node children by the best axis for split
		private static void ChooseSplitAxis(RTreeNode<T> node, int m, int M)
		{
			var xMargin = AllDistMargin(node, m, M, CompareNodesByMinX);
			var yMargin = AllDistMargin(node, m, M, CompareNodesByMinY);

			// if total distributions margin value is minimal for x, sort by minX,
			// otherwise it's already sorted by minY
			if (xMargin < yMargin) node.Children.Sort(CompareNodesByMinX);
		}

		private static int CompareNodesByMinX(RTreeNode<T> a, RTreeNode<T> b) { return a.Envelope.X1.CompareTo(b.Envelope.X1); }
		private static int CompareNodesByMinY(RTreeNode<T> a, RTreeNode<T> b) { return a.Envelope.Y1.CompareTo(b.Envelope.Y1); }

		private static int AllDistMargin(RTreeNode<T> node, int m, int M, Comparison<RTreeNode<T>> compare)
		{
			node.Children.Sort(compare);

			var leftBBox = SumChildBounds(node, 0, m);
			var rightBBox = SumChildBounds(node, M - m, M);
			var margin = leftBBox.Margin + rightBBox.Margin;

			for (var i = m; i < M - m; i++)
			{
				var child = node.Children[i];
				leftBBox.Extend(child.Envelope);
				margin += leftBBox.Margin;
			}

			for (var i = M - m - 1; i >= m; i--)
			{
				var child = node.Children[i];
				rightBBox.Extend(child.Envelope);
				margin += rightBBox.Margin;
			}

			return margin;
		}
	}
}

#region [ License information          ]

/* ************************************************************
 *
 *    Copyright (c) Attila Kiskó, enyim.com
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/

#endregion
