(async () => {
    const check = (value, message) => { if (!value) throw new Error(message); };
    const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
    check(new URL(API_BASE_URL, location.href).origin === location.origin, 'API URL is not the shop origin; unsafe to run signup against another database');
    check(currentUser === null, 'Broken cached session did not recover to signed out');
    const email = `browser-${crypto.randomUUID()}@example.test`;
    const password = 'Browser-Smoke-Password!123';
    document.getElementById('toRegisterLink').click();
    document.getElementById('registerName').value = 'Browser Smoke';
    document.getElementById('registerEmail').value = email;
    document.getElementById('registerPassword').value = password;
    await handleRegister({ preventDefault() {} });
    check(document.getElementById('loginForm').style.display !== 'none', 'Registration did not return to login');
    document.getElementById('loginEmail').value = email;
    document.getElementById('loginPassword').value = password;
    await handleLogin({ preventDefault() {} });
    check(currentUser?.token, 'Login failed');
    await pause(1100);
    await loadProducts();
    const button = document.querySelector('.btn-add-cart');
    check(button, 'Product list is empty');
    const product = Number(button.getAttribute('onclick').split('(')[1].split(',')[0]);
    await addToCart(product, button);
    check(cart.items.length === 1 && cart.items[0].quantity === 1, 'Cart add failed');
    showSection('checkout');
    document.getElementById('address').value = 'Test delivery address';
    document.getElementById('city').value = 'Test city';
    await handleCheckout({ preventDefault() {} });
    const attempt = JSON.parse(sessionStorage.getItem(`order-create:${currentUser.id}`));
    check(attempt?.orderId, 'Checkout failed');
    await pause(1100);
    check(Number(document.getElementById('paymentOrderId').value) === attempt.orderId, 'Payment form did not receive order');
    // Observe actual network payload without mocking the HTTP response.
    const originalFetch = window.fetch;
    let paymentPayload;
    let paymentRequests = 0;
    window.fetch = async (...args) => {
        if (String(args[0]).endsWith('/payments')) { paymentPayload = JSON.parse(args[1].body); paymentRequests++; }
        return originalFetch(...args);
    };
    try { await Promise.all([handlePayment({ preventDefault() {} }), handlePayment({ preventDefault() {} })]); }
    finally { window.fetch = originalFetch; }
    check(paymentPayload && Object.keys(paymentPayload).sort().join(',') === 'amount,orderId', 'Payment unnecessarily transmits card data');
    check(paymentRequests === 1, 'Double submit issued duplicate payment HTTP calls');
    await pause(1600);
    await loadOrders();
    const response = await fetch(`${API_BASE_URL}/orders/mine`, { headers: authHeaders() });
    check(response.ok, 'Order history API failed');
    const orders = await response.json();
    check(orders.length === 1 && orders[0].status === 'PAID', 'Order did not reach PAID');
    check(document.querySelectorAll('.order-card').length === 1, 'History UI did not render');
    check(cart.items.length === 0, 'Cart was not cleared');
    check(sessionStorage.getItem(`order-create:${currentUser.id}`) === null, 'Completed checkout key was not cleared');
    return { registration: true, login: true, products: true, cart: true, checkout: true, payment: 'SUCCESS', history: 'PAID', card_data_sent: false, broken_cache_recovered: true, duplicate_payment_submit_blocked: true };
})()
