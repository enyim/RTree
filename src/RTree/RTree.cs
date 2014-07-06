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

		public void Load(IReadOnlyList<RTreeNode<T>> nodes)
		{
			if (nodes.Count < minEntries)
			{
				foreach (var n in nodes) Insert(n);

				return;
			}

			// recursively build the tree with the given data from stratch using OMT algorithm
			var node = BuildOneLevel(nodes.ToList(), 0, 0);

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

		public IEnumerable<T> All()
		{
			var retval = new List<T>();
			Collect(root, retval);

			return retval;
		}

		public IEnumerable<T> Search(Envelope envelope)
		{
			var node = root;

			if (!envelope.Intersects(node.Envelope)) return Enumerable.Empty<T>();

			var retval = new List<T>();
			var nodesToSearch = new Stack<RTreeNode<T>>();

			while (node != null)
			{
				for (var i = 0; i < node.Children.Count; i++)
				{
					var child = node.Children[i];
					var childEnvelope = child.Envelope;

					if (envelope.Intersects(childEnvelope))
					{
						if (node.IsLeaf) retval.Add(child.Data);
						else if (envelope.Contains(childEnvelope)) Collect(child, retval);
						else nodesToSearch.Push(child);
					}
				}

				node = nodesToSearch.TryPop();
			}

			return retval;
		}

		private void Collect(RTreeNode<T> node, List<T> result)
		{
			var nodesToSearch = new Stack<RTreeNode<T>>();
			while (node != null)
			{
				if (node.IsLeaf) result.AddRange(node.Children.Select(n => n.Data));
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
				if (insertPath[level].Children.Count > maxEntries)
				{
					Split(insertPath, level);
					level--;
				}
				else break;
			}

			// adjust bboxes along the insertion path
			AdjutsParentBounds(envelope, insertPath, level);
		}

		private int EnlargedArea(Envelope what, Envelope by)
		{
			return (Math.Max(by.X2, what.X2) - Math.Min(by.X1, what.X1)) *
					(Math.Max(by.Y2, what.Y2) - Math.Min(by.Y1, what.Y1));
		}

		private int IntersectionArea(Envelope what, Envelope with)
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
					var enlargement = EnlargedArea(bbox, child.Envelope) - area;

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
			var M = node.Children.Count;
			var m = minEntries;

			ChooseSplitAxis(node, m, M);

			var newNode = new RTreeNode<T> { Height = node.Height };
			var splitIndex = ChooseSplitIndex(node, m, M);

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

		private int ChooseSplitIndex(RTreeNode<T> node, int m, int M)
		{
			var minOverlap = Int32.MaxValue;
			var minArea = Int32.MaxValue;
			int index = 0;

			for (var i = m; i <= M - m; i++)
			{
				var bbox1 = SumChildBounds(node, 0, i);
				var bbox2 = SumChildBounds(node, i, M);

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

		// sorts node children by the best axis for split
		void ChooseSplitAxis(RTreeNode<T> node, int m, int M)
		{

			var xMargin = _allDistMargin(node, m, M, CompareNodesByMinX);
			var yMargin = _allDistMargin(node, m, M, CompareNodesByMinY);

			// if total distributions margin value is minimal for x, sort by minX,
			// otherwise it's already sorted by minY
			if (xMargin < yMargin) node.Children.Sort(CompareNodesByMinX);
		}

		int CompareNodesByMinX(RTreeNode<T> a, RTreeNode<T> b) { return a.Envelope.X1.CompareTo(b.Envelope.X1); }
		int CompareNodesByMinY(RTreeNode<T> a, RTreeNode<T> b) { return a.Envelope.Y1.CompareTo(b.Envelope.Y1); }

		int compareMinX(Envelope a, Envelope b) { return a.X1.CompareTo(b.X1); }
		int compareMinY(Envelope a, Envelope b) { return a.Y1.CompareTo(b.Y1); }


		int _allDistMargin(RTreeNode<T> node, int m, int M, Comparison<RTreeNode<T>> compare)
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

		// calculate node's bbox from bboxes of its children
		private void RefreshEnvelope(RTreeNode<T> node)
		{
			node.Envelope = SumChildBounds(node, 0, node.Children.Count);
		}

		private Envelope SumChildBounds(RTreeNode<T> node, int startIndex, int endIndex)
		{
			var retval = new Envelope();

			for (var i = startIndex; i < endIndex; i++)
			{
				retval.Extend(node.Children[i].Envelope);
			}

			return retval;
		}

		private void AdjutsParentBounds(Envelope bbox, List<RTreeNode<T>> path, int level)
		{
			// adjust bboxes along the given tree path
			for (var i = level; i >= 0; i--)
			{
				path[i].Envelope.Extend(bbox);
			}
		}

		public void Remove(RTreeNode<T> item)
		{
			var node = root;
			var bbox = item.Envelope;
			var path = new Stack<RTreeNode<T>>();
			var indexes = new Stack<int>();

			RTreeNode<T> parent = null;
			var i = 0;
			var goingUp = false;

			// depth-first iterative tree traversal
			while (node != null || path.Count > 0)
			{

				if (node == null)
				{ // go up

					node = path.TryPop();
					parent = path.TryPeek();
					i = indexes.TryPop();

					goingUp = true;
				}

				if (node != null && node.IsLeaf)
				{
					// check current node
					var index = node.Children.IndexOf(item);

					if (index != -1)
					{
						// item found, remove the item and condense tree upwards
						node.Children.RemoveAt(index);
						path.Push(node);
						CondenseNodes(path.ToArray());
						return;
					}
				}

				if (!goingUp && !node.IsLeaf && node.Envelope.Contains(bbox))
				{ // go down
					path.Push(node);
					indexes.Push(i);
					i = 0;
					parent = node;
					node = node.Children[0];

				}
				else if (parent != null)
				{ // go right
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
	}

	internal static class StackHelpers
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T TryPop<T>(this Stack<T> stack)
		{
			return stack.Count == 0 ? default(T) : stack.Pop();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T TryPeek<T>(this Stack<T> stack)
		{
			return stack.Count == 0 ? default(T) : stack.Peek();
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
