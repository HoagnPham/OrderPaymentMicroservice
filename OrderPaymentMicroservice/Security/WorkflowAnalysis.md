# Order & Payment Workflow Analysis - For Formal Verification

## 1. ORDER STATE MACHINE

### Order Status States (from OrderStatus enum):
```
CREATED (0)
  ↓
VALIDATED (1)
  ↓
PAYMENT_PENDING (2)
  ↓
PAID (3)
  ↓
CONFIRMED (4)
  ↓
COMPLETED (5)

Side paths:
CANCELLED (6) - can transition from any state
FAILED (7) - can transition from PAYMENT_PENDING, PAID
```

### Required Properties (from README & TODOs):
1. **Safety Property (Safety)**: Order must follow state transitions in order
   - `CREATED → VALIDATED → PAYMENT_PENDING → PAID → CONFIRMED → COMPLETED`
   - No backward transitions allowed (except CANCELLED/FAILED)
   - No skipping states allowed

2. **Liveness Property**: Every order must eventually complete or fail/cancel
   - `∀order: eventually (order.status ∈ {COMPLETED, CANCELLED, FAILED})`

3. **Consistency Property**: Order cannot have invalid state combinations
   - If `status == PAID`, then ∃payment with `status == SUCCESS`
   - If `status == PAYMENT_PENDING`, then no payment.status == SUCCESS yet

---

## 2. PAYMENT STATE MACHINE

### Payment Status States:
```
PENDING (0)
  ↓
SUCCESS (1) or FAILED (2)
```

### Required Properties:
1. **Safety**: Payment statuses must be valid
   - `PENDING → SUCCESS` or `PENDING → FAILED` only

2. **Consistency with Order**:
   - Order transitions to PAID iff Payment.status == SUCCESS
   - If Payment.status == FAILED, Order cannot be PAID

---

## 3. ARITHMETIC CONSTRAINTS (for SMT/Z3 verification)

### Order Amount Constraints:
```
1. TotalAmount > 0 (non-negative requirement)
2. TotalAmount == SUM(OrderItems.quantity * OrderItems.unitPrice)
   - This is a SUM constraint that Z3 should verify
3. TotalAmount must not overflow (decimal.MaxValue check)
```

### Payment Constraints:
```
1. Amount > 0
2. Amount <= Order.TotalAmount (payment cannot exceed order amount)
3. SUM(all payments for Order) <= Order.TotalAmount
   - Partial payments are allowed, but sum cannot exceed order total
4. If Order.status == PAID, then SUM(Payments where status==SUCCESS) == Order.TotalAmount
   - All money must be accounted for
```

---

## 4. IDENTIFIED SECURITY/CORRECTNESS ISSUES FOR VERIFICATION

From code review & API testing:

### Issue #1: Order POST Returns 400
- **Current**: Order creation endpoint validates but fails
- **Property to Verify**: Inputs must be validated before state transition
- **Test**: Fuzzing with invalid amounts, negative quantities

### Issue #2: TotalAmount vs OrderItems Sum
- **Property**: TotalAmount must equal sum of items
- **Risk**: Payment processing could be wrong if amount doesn't match
- **Test**: SMT/Z3 constraint: TotalAmount ∈ calculation

### Issue #3: Partial Payments
- **Property**: Multiple payments allowed but sum ≤ TotalAmount
- **Edge Case**: What if two concurrent payments both claim full amount?
- **Test**: Concurrency fuzzing, overflow detection

### Issue #4: Invalid State Transitions
- **Property**: State must follow defined flow
- **Risk**: Manual DB updates could violate state machine
- **Test**: BMC model checking to enforce order

### Issue #5: CANCELLED/FAILED from any state
- **Property**: CANCELLED can come from any state, but what about partial payments?
- **Risk**: If order is CANCELLED after PAID, refund logic is unclear
- **Test**: Verify CANCELLED state handling after payments

---

## 5. MAPPING TO FORMAL VERIFICATION TOOLS

### Chapter 1: Kripke + BMC (Model Checking)
**Tool**: TLA+ or Alloy
**Model**: OrderPaymentStateMachine.tla
```
MODULE OrderPayment
  - Define states: {CREATED, VALIDATED, ..., COMPLETED, CANCELLED, FAILED}
  - Define transitions: allowed_transitions(s1, s2)
  - Define invariants: no_backward_transitions(), no_skipped_states()
  - Define safety: G(state_sequence_valid())
  - Define liveness: G(F(terminal_state))
  - Run BMC to check for counterexamples
```

### Chapter 2: Memory Safety
**Tool**: Roslyn Analyzer / FxCop
**Check**: null-reference, use-after-free, buffer overflows
- Verify Order.User is not null when accessed
- Verify OrderItems list is not null after creation
- Verify Payments collection proper initialization

### Chapter 3: SMT/Z3 (Arithmetic Verification)
**Tool**: Z3 Solver
**Constraints**:
```
assert(TotalAmount > 0)
assert(TotalAmount == sum(OrderItems[i].quantity * OrderItems[i].unitPrice))
assert(sum(Payment[i].Amount) <= TotalAmount)
assert(!(status == PAID && !∃p: p.status == SUCCESS))
```

### Chapter 4: Fuzzing / Dynamic Testing
**Tool**: FsCheck (Property-Based Testing) or LibFuzzer
**Tests**:
- Random order amounts (+, -, 0, max)
- Random item quantities and prices
- Random payment sequences
- Random state transitions
- Edge cases: overflow, underflow, precision loss
---

## 6. NEXT STEPS

1. ✅ Analyze workflow (this document)
2. Create Kripke model (TLA+) → run BMC
3. Create Z3 constraints → run solver
4. Create FsCheck property tests or fuzzer
5. Document findings & create counterexamples
6. Fix issues found
7. Re-verify fixed code
8. Write security report
