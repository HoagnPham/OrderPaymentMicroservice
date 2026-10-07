; Order/payment arithmetic verification.
; Monetary values are represented as integer cents. QF_NIA is required
; because Quantity * UnitPriceCents multiplies two symbolic integers.
(set-logic QF_NIA)
(set-option :produce-models true)

(declare-const Quantity1 Int)
(declare-const UnitPrice1Cents Int)
(declare-const Quantity2 Int)
(declare-const UnitPrice2Cents Int)
(declare-const TotalAmountCents Int)
(declare-const Payment1Cents Int)
(declare-const Payment2Cents Int)
(declare-const OrderStatus Int)

(define-fun ValidItem ((quantity Int) (unitPriceCents Int)) Bool
  (and (<= 1 quantity) (<= quantity 10000)
       (<= 1 unitPriceCents) (<= unitPriceCents 99999999)))

(define-fun AmountInvariant () Bool
  (and (ValidItem Quantity1 UnitPrice1Cents)
       (ValidItem Quantity2 UnitPrice2Cents)
       (= TotalAmountCents
          (+ (* Quantity1 UnitPrice1Cents)
             (* Quantity2 UnitPrice2Cents)))
       (<= 1 TotalAmountCents)
       (<= TotalAmountCents 99999999)))

(define-fun PaymentInvariant () Bool
  (and (<= 0 Payment1Cents)
       (<= 0 Payment2Cents)
       (<= (+ Payment1Cents Payment2Cents) TotalAmountCents)
       (=> (= OrderStatus 3)
           (= (+ Payment1Cents Payment2Cents) TotalAmountCents))))

(echo "TEST1_VALID_PAID")
(push)
(assert (= Quantity1 2))
(assert (= UnitPrice1Cents 50000))
(assert (= Quantity2 1))
(assert (= UnitPrice2Cents 40000))
(assert (= TotalAmountCents 140000))
(assert (= Payment1Cents 140000))
(assert (= Payment2Cents 0))
(assert (= OrderStatus 3))
(assert AmountInvariant)
(assert PaymentInvariant)
(check-sat)
(get-value (TotalAmountCents Payment1Cents Payment2Cents))
(pop)

(echo "TEST2_TOTAL_MISMATCH")
(push)
(assert (= Quantity1 2))
(assert (= UnitPrice1Cents 50000))
(assert (= Quantity2 1))
(assert (= UnitPrice2Cents 40000))
(assert (= TotalAmountCents 100000))
(assert AmountInvariant)
(check-sat)
(pop)

(echo "TEST3_PAID_UNDERPAYMENT")
(push)
(assert (= TotalAmountCents 140000))
(assert (= Payment1Cents 100000))
(assert (= Payment2Cents 0))
(assert (= OrderStatus 3))
(assert PaymentInvariant)
(check-sat)
(pop)

(echo "TEST4_NEGATIVE_PAYMENT")
(push)
(assert (= TotalAmountCents 140000))
(assert (= Payment1Cents (- 1)))
(assert (= Payment2Cents 0))
(assert (= OrderStatus 2))
(assert PaymentInvariant)
(check-sat)
(pop)

(echo "TEST5_PAYMENT_EXCEEDS_TOTAL")
(push)
(assert (= TotalAmountCents 100000))
(assert (= Payment1Cents 150000))
(assert (= Payment2Cents 0))
(assert (= OrderStatus 2))
(assert PaymentInvariant)
(check-sat)
(pop)

; Counterexample: allowed item bounds can overflow a signed 32-bit subtotal.
(echo "TEST6_INT32_SUBTOTAL_OVERFLOW_EXISTS")
(push)
(assert (ValidItem Quantity1 UnitPrice1Cents))
(assert (> (* Quantity1 UnitPrice1Cents) 2147483647))
(check-sat)
(get-value (Quantity1 UnitPrice1Cents (* Quantity1 UnitPrice1Cents)))
(pop)

; With the application total bound and positive items, an oversized subtotal
; is impossible in a valid order.
(echo "TEST7_TOTAL_BOUND_BLOCKS_OVERSIZED_SUBTOTAL")
(push)
(assert AmountInvariant)
(assert (> (* Quantity1 UnitPrice1Cents) 99999999))
(check-sat)
(pop)
