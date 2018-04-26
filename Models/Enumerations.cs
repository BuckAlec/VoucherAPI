using System;
using System.ComponentModel;
using System.Reflection;

namespace FortressCodesApi.Models
{
    public class Enumerations
    {
        public enum VoucherType
        {
            Voucher = 1,
            Promo = 2,
            Gateway = 3,
            Deductible = 4,
            Billing = 5,
            Subscription = 7
        }

        public enum MethodName
        {
            [Description("Validate")]
            Validate,
            [Description("Activate")]
            Activate,
            [Description("ChangeCoverage")]
            ChangeCoverage,
            [Description("PaymentGatewayCoverage")]
            PaymentGatewayCoverage,
            [Description("ProcessCharge")]
            ProcessCharge,
            [Description("ProcessResponseCharge")]
            ProcessResponseCharge,
            [Description("StripeRefundCover")]
            StripeRefundCover,
            [Description("CoverValidation")]
            CoverValidation
        }

        public enum TransactionType
        {
            [Description("Validation Request")]
            ValidationRequest,
            [Description("Validated")]
            Validated,
            [Description("In Use")]
            InUse,
            [Description("Locked")]
            Locked,
            [Description("Maximum Use")]
            MaximumUse,
            [Description("Time Limit")]
            TimeLimit,
            [Description("Error")]
            Error,
            [Description("Activation Request")]
            ActivationRequest,
            [Description("Activated")]
            Activated,
            [Description("Cancellation Approved")]
            CancellationApproved,
            [Description("Cancellation Declined")]
            CancellationDeclined,
            [Description("Cancelled")]
            Cancelled,
            [Description("Cancellation Request")]
            CancellationRequest,
            [Description("Returned")]
            Returned,
            [Description("Category Mismatch")]
            CategoryMismatch,
            [Description("Territory Mismatch")]
            TerritoryMismatch,
            [Description("Invalid voucher code")]
            InvalidVoucherCode,
            [Description("Device Level Request")]
            DeviceLevelRequest,
            [Description("No Device Level")]
            NoDeviceLevel,
            [Description("Gateway Voucher Alteration")]
            GatewayVoucherAlteration,
            [Description("Created")]
            Created,
            [Description("Missing Family Level")]
            MissingFamilyLevel,
            [Description("Change Coverage")]
            ChangeCoverage,
            [Description("Missing Device")]
            MissingDevice,
            [Description("Missing Device Activation")]
            MissingDeviceActivation,
            [Description("Grace Period")]
            GracePeriod,
            [Description("Payment Gateway Coverage")]
            PaymentGatewayCoverage,
            [Description("Process Charge")]
            ProcessCharge,
            [Description("Process Charge Response")]
            ProcessChargeResponse,
            [Description("Process Charge Error")]
            ProcessChargeError,
            [Description("Cover Validation Mem")]
            CoverValidationMem,            
            [Description("Vouchers cannot be used with active subscription")]
            ActiveSubscription,
            [Description("Paid For Zero Cover Applied")]
            VoucherZeroCoverPaid
        }

        public enum CodeResponseMessage
        {
            [Description("Success")]
            Success,
            [Description("Your device type is not on our system. Your membership will be updated to the correct number of days when we update our records.")]
            MissingDevice,
            [Description("Your days of protection has been adjusted to reflect the change in either device value or membership tier.  For further information please visit www.yourfortress.com")]
            ProRata,
        }

        public static string GetEnumDescription(Enum value)
        {
            FieldInfo fi = value.GetType().GetField(value.ToString());

            DescriptionAttribute[] attributes =
                (DescriptionAttribute[])fi.GetCustomAttributes(
                typeof(DescriptionAttribute),
                false);

            if (attributes != null &&
                attributes.Length > 0)
                return attributes[0].Description;
            else
                return value.ToString();
        }
    }
}