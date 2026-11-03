using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class SupplierReturn
{
    public int SupplierReturnId { get; set; }

    public string SupplierReturnCode { get; set; } = null!;

    public int SupplierId { get; set; }

    public int? GoodsReceiptId { get; set; }

    public int CreatedByEmployeeId { get; set; }

    public DateTime ReturnDate { get; set; }

    public string Reason { get; set; } = null!;

    public byte Status { get; set; }

    public decimal TotalAmount { get; set; }

    public virtual Employee CreatedByEmployee { get; set; } = null!;

    public virtual GoodsReceipt? GoodsReceipt { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;

    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();

    public virtual ICollection<SupplierReturnDetail> SupplierReturnDetails { get; set; } = new List<SupplierReturnDetail>();
}
