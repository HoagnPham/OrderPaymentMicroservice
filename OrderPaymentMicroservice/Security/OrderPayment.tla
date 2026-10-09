---------------------------- MODULE OrderPayment ----------------------------
EXTENDS Naturals

(***************************************************************************
 One-order abstraction of the implementation state machines. Order and
 payment states are deliberately separate. PROCESSING/UNKNOWN describe the
 payment operation while the order remains PAYMENT_PENDING.
***************************************************************************)

CONSTANT MaxAttempts
ASSUME MaxAttempts \in Nat \ {0}

OrderStatuses == {
    "CREATED", "VALIDATED", "PAYMENT_PENDING", "PAID", "CONFIRMED",
    "COMPLETED", "CANCELLED", "FAILED", "REFUND_PENDING", "REFUNDED"
}

PaymentStatuses == {
    "NONE", "PENDING", "PROCESSING", "UNKNOWN", "SUCCESS", "FAILED",
    "REFUND_PENDING", "REFUNDED"
}

InventoryStatuses == {"UNRESERVED", "RESERVED", "RELEASED", "CONSUMED"}
TerminalOrderStatuses == {"COMPLETED", "CANCELLED", "FAILED", "REFUNDED"}
PaymentEvidence == {"SUCCESS", "REFUND_PENDING", "REFUNDED"}

VARIABLES orderStatus, paymentStatus, inventoryStatus, attemptCount
vars == <<orderStatus, paymentStatus, inventoryStatus, attemptCount>>

Init ==
    /\ orderStatus = "CREATED"
    /\ paymentStatus = "NONE"
    /\ inventoryStatus = "UNRESERVED"
    /\ attemptCount = 0

Validate ==
    /\ orderStatus = "CREATED"
    /\ orderStatus' = "VALIDATED"
    /\ inventoryStatus' = "RESERVED"
    /\ UNCHANGED <<paymentStatus, attemptCount>>

PersistOrder ==
    /\ orderStatus = "VALIDATED"
    /\ orderStatus' = "PAYMENT_PENDING"
    /\ UNCHANGED <<paymentStatus, inventoryStatus, attemptCount>>

StartPayment ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "NONE"
    /\ paymentStatus' = "PROCESSING"
    /\ attemptCount' = 1
    /\ UNCHANGED <<orderStatus, inventoryStatus>>

ChargeSucceeded ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "PROCESSING"
    /\ orderStatus' = "PAID"
    /\ paymentStatus' = "SUCCESS"
    /\ UNCHANGED <<inventoryStatus, attemptCount>>

ChargeDeclined ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "PROCESSING"
    /\ orderStatus' = "FAILED"
    /\ paymentStatus' = "FAILED"
    /\ inventoryStatus' = "RELEASED"
    /\ UNCHANGED attemptCount

ChargeOutcomeUnknown ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "PROCESSING"
    /\ paymentStatus' = "UNKNOWN"
    /\ UNCHANGED <<orderStatus, inventoryStatus, attemptCount>>

RetryUnknownPayment ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "UNKNOWN"
    /\ attemptCount < MaxAttempts
    /\ paymentStatus' = "PROCESSING"
    /\ attemptCount' = attemptCount + 1
    /\ UNCHANGED <<orderStatus, inventoryStatus>>

ReconcileUnknownSucceeded ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "UNKNOWN"
    /\ orderStatus' = "PAID"
    /\ paymentStatus' = "SUCCESS"
    /\ UNCHANGED <<inventoryStatus, attemptCount>>

ReconcileUnknownDeclined ==
    /\ orderStatus = "PAYMENT_PENDING"
    /\ paymentStatus = "UNKNOWN"
    /\ orderStatus' = "FAILED"
    /\ paymentStatus' = "FAILED"
    /\ inventoryStatus' = "RELEASED"
    /\ UNCHANGED attemptCount

CancelBeforeCharge ==
    /\ orderStatus \in {"CREATED", "VALIDATED", "PAYMENT_PENDING"}
    /\ paymentStatus \in {"NONE", "PENDING", "FAILED"}
    /\ orderStatus' = "CANCELLED"
    /\ inventoryStatus' = "RELEASED"
    /\ UNCHANGED <<paymentStatus, attemptCount>>

ConfirmOrder ==
    /\ orderStatus = "PAID"
    /\ paymentStatus = "SUCCESS"
    /\ orderStatus' = "CONFIRMED"
    /\ UNCHANGED <<paymentStatus, inventoryStatus, attemptCount>>

CompleteOrder ==
    /\ orderStatus = "CONFIRMED"
    /\ paymentStatus = "SUCCESS"
    /\ orderStatus' = "COMPLETED"
    /\ inventoryStatus' = "CONSUMED"
    /\ UNCHANGED <<paymentStatus, attemptCount>>

StartRefund ==
    /\ orderStatus \in {"PAID", "CONFIRMED"}
    /\ paymentStatus = "SUCCESS"
    /\ orderStatus' = "REFUND_PENDING"
    /\ paymentStatus' = "REFUND_PENDING"
    /\ UNCHANGED <<inventoryStatus, attemptCount>>

RefundSucceeded ==
    /\ orderStatus = "REFUND_PENDING"
    /\ paymentStatus = "REFUND_PENDING"
    /\ orderStatus' = "REFUNDED"
    /\ paymentStatus' = "REFUNDED"
    /\ inventoryStatus' = "RELEASED"
    /\ UNCHANGED attemptCount

TerminalStutter ==
    /\ orderStatus \in TerminalOrderStatuses
    /\ UNCHANGED vars

Next ==
    \/ Validate
    \/ PersistOrder
    \/ StartPayment
    \/ ChargeSucceeded
    \/ ChargeDeclined
    \/ ChargeOutcomeUnknown
    \/ RetryUnknownPayment
    \/ ReconcileUnknownSucceeded
    \/ ReconcileUnknownDeclined
    \/ CancelBeforeCharge
    \/ ConfirmOrder
    \/ CompleteOrder
    \/ StartRefund
    \/ RefundSucceeded
    \/ TerminalStutter

TypeOK ==
    /\ orderStatus \in OrderStatuses
    /\ paymentStatus \in PaymentStatuses
    /\ inventoryStatus \in InventoryStatuses
    /\ attemptCount \in 0..MaxAttempts

PaidStatesHavePaymentEvidence ==
    orderStatus \in {"PAID", "CONFIRMED", "COMPLETED", "REFUND_PENDING", "REFUNDED"}
        => paymentStatus \in PaymentEvidence

CancelledHasNoSuccessfulCharge ==
    orderStatus = "CANCELLED" => paymentStatus \notin PaymentEvidence

FailedHasNoSuccessfulCharge ==
    orderStatus = "FAILED" => paymentStatus = "FAILED"

InventoryLifecycleIsConsistent ==
    /\ (orderStatus \in {"CANCELLED", "FAILED", "REFUNDED"} => inventoryStatus = "RELEASED")
    /\ (orderStatus = "COMPLETED" => inventoryStatus = "CONSUMED")
    /\ (orderStatus \in {"VALIDATED", "PAYMENT_PENDING", "PAID", "CONFIRMED", "REFUND_PENDING"}
        => inventoryStatus = "RESERVED")

Terminal == orderStatus \in TerminalOrderStatuses
Termination == <>Terminal

Spec == Init /\ [][Next]_vars /\ WF_vars(Next)

=============================================================================
