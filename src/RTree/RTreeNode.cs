using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Enyim.Collections
{
	public class RTreeNode<T>
	{
		private readonly Lazy<List<RTreeNode<T>>> children;

		internal RTreeNode() : this(default(T), new Envelope()) { }

		public RTreeNode(T data, Envelope envelope)
		{
			Data = data;
			Envelope = envelope;
			children = new Lazy<List<RTreeNode<T>>>(() => new List<RTreeNode<T>>(), LazyThreadSafetyMode.None);
		}

		public T Data { get; private set; }
		public Envelope Envelope { get; internal set; }

		internal bool IsLeaf { get; set; }
		internal int Height { get; set; }
		internal List<RTreeNode<T>> Children { get { return children.Value; } }
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
