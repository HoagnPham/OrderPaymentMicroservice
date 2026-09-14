(set-logic QF_LIA)  ;; Quantifier-Free Linear Integer Arithmetic

;; ORDER STATE MACHINE VERIFICATION IN Z3

;; Define order amount constraint
;; Property: TotalAmount must be positive and equal sum of items
(declare-const TotalAmount Int)
(declare-const OrderItem1_Qty Int)
(declare-const OrderItem1_Price Int)
(declare-const OrderItem2_Qty Int)
(declare-const OrderItem2_Price Int)

;; Define payment amounts
(declare-const Payment1_Amount Int)
(declare-const Payment2_Amount Int)

;; Declare order status (0=CREATED, 1=VALIDATED, 2=PAYMENT_PENDING, 3=PAID, 4=CONFIRMED, 5=COMPLETED)
(declare-const OrderStatus Int)

;; ===== CONSTRAINT 1: TotalAmount must be positive =====
(assert (> TotalAmount 0))

;; ===== CONSTRAINT 2: TotalAmount = sum(items) =====
;; This is the critical invariant for payment correctness
(assert (= TotalAmount (+ (* OrderItem1_Qty OrderItem1_Price) 
						   (* OrderItem2_Qty OrderItem2_Price))))

;; ===== CONSTRAINT 3: Item quantities must be non-negative =====
(assert (>= OrderItem1_Qty 0))
(assert (>= OrderItem2_Qty 0))
(assert (>= OrderItem1_Price 0))
(assert (>= OrderItem2_Price 0))

;; ===== CONSTRAINT 4: Payment amounts must be positive =====
(assert (>= Payment1_Amount 0))
(assert (>= Payment2_Amount 0))

;; ===== CONSTRAINT 5: Sum of payments cannot exceed order total =====
(assert (<= (+ Payment1_Amount Payment2_Amount) TotalAmount))

;; ===== CONSTRAINT 6: Order can only be PAID if sum of payments equals total =====
;; (This would require reasoning over conditionals, which is more complex)
;; For now, we verify if order is in PAID state, then payments must cover full amount
(assert (
	=> (= OrderStatus 3)  ;; if OrderStatus == PAID
	   (= (+ Payment1_Amount Payment2_Amount) TotalAmount)  ;; then total payments must equal order
))

;; ===== TEST CASE 1: Valid scenario =====
;; Try to find a satisfying assignment
(push)
(assert (= OrderItem1_Qty 2))
(assert (= OrderItem1_Price 50000))
(assert (= OrderItem2_Qty 1))
(assert (= OrderItem2_Price 40000))
(assert (= OrderStatus 3))  ;; PAID
(check-sat)
(get-model)
(pop)

;; ===== TEST CASE 2: Invalid scenario - TotalAmount mismatch =====
;; Try to create inconsistency: intentionally set wrong total
(push)
(assert (= OrderItem1_Qty 2))
(assert (= OrderItem1_Price 50000))
(assert (= OrderItem2_Qty 1))
(assert (= OrderItem2_Price 40000))
(assert (= TotalAmount 100000))  ;; WRONG: should be 140000
(check-sat)
(get-model)
(pop)

;; ===== TEST CASE 3: Overflow scenario =====
;; Check if large numbers cause issues
(push)
(assert (= OrderItem1_Qty 1000000))
(assert (= OrderItem1_Price 10000000))
(assert (= OrderItem2_Qty 1000000))
(assert (= OrderItem2_Price 10000000))
(check-sat)
(get-model)
(pop)

;; ===== TEST CASE 4: Negative amount =====
;; Try to set negative payment (should be unsatisfiable)
(push)
(assert (= Payment1_Amount -50000))
(check-sat)
(pop)

;; ===== TEST CASE 5: Payment exceeds order =====
(push)
(assert (= TotalAmount 100000))
(assert (= Payment1_Amount 150000))
(check-sat)
(get-model)
(pop)
