using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UIM.MVVM
{
    public class ItemEventArgs : EventArgs
    {
		private List<Guid> m_ids;

		public List<Guid> Ids
		{
			get { return m_ids; }
		}

		public ItemEventArgs(List<Guid> ids)
		{
			m_ids = ids;
		}
	}


}
