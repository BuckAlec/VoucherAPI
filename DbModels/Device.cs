namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("Device")]
    public partial class Device
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Device()
        {
            DeviceLevels = new HashSet<DeviceLevel>();
        }

        public int id { get; set; }

        [StringLength(50)]
        public string name_raw { get; set; }

        [StringLength(50)]
        public string name { get; set; }

        public int? CountryId { get; set; }

        public int? PartnerID { get; set; }

        public virtual Country Country { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<DeviceLevel> DeviceLevels { get; set; }
    }
}
