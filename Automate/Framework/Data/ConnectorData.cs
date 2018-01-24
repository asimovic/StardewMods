using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pathoschild.Stardew.Automate.Framework.Data
{
    internal class ConnectorData
    {
        public string Name { get; set; }
        public int ItemId { get; set; }
        public int FlooringId { get; set; }

        public override string ToString()
        {
            return $"{this.Name} (iid:{this.ItemId}, fid:{this.FlooringId}";
        }
    }
}
