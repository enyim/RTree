using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Enyim.Collections
{
	public class Envelope
	{
		public Envelope() { }

		public Envelope(int x1, int y1, int x2, int y2)
		{
			X1 = x1;
			Y1 = y1;
			X2 = x2;
			Y2 = y2;
		}

		public int X1 { get; private set; } // 0
		public int Y1 { get; private set; } // 1
		public int X2 { get; private set; } // 2
		public int Y2 { get; private set; } // 3

		public int Area { get { return (X2 - X1) * (Y2 - Y1); } }
		public int Margin { get { return (X2 - X1) + (Y2 - Y1); } }

		public void Extend(Envelope by)
		{
			X1 = Math.Min(X1, by.X1);
			Y1 = Math.Min(Y1, by.Y1);
			X2 = Math.Max(X2, by.X2);
			Y2 = Math.Max(Y2, by.Y2);
		}

		public override string ToString()
		{
			return String.Format("{0},{1} - {2},{3}", X1, Y1, X2, Y2);
		}

		public bool Intersects(Envelope b)
		{
			return b.X1 <= X2 && b.Y1 <= Y2 &&
					b.X2 >= X1 && b.Y2 >= Y1;
		}

		public bool Contains(Envelope b)
		{
			return X1 <= b.X1 && Y1 <= b.Y1 &&
					b.X2 <= X2 && b.Y2 <= Y2;
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
