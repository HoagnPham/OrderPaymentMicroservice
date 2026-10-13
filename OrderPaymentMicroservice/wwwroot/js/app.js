// API Configuration
const API_BASE_URL = '/api';
let currentUser = null;
let cart = { items: [], totalPrice: 0 };

const foodStyles = [
    { glyph: '🍜', a: '#f7d9a7', b: '#e9a85e' },
    { glyph: '🍚', a: '#d9e7c5', b: '#91ba7c' },
    { glyph: '🥖', a: '#f2d2b1', b: '#d98a58' },
    { glyph: '🍊', a: '#ffd9a0', b: '#f3a13b' },
    { glyph: '☕', a: '#d8c5b5', b: '#9b735d' },
    { glyph: '🍨', a: '#e3d5ef', b: '#ac83ce' }
];

function formatCurrency(value) {
    return `${Number(value || 0).toLocaleString('vi-VN')} VNĐ`;
}

function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>'"]/g, char => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
    })[char]);
}

function getInitials(name) {
    return String(name || 'U').trim().split(/\s+/).slice(-2).map(part => part[0]).join('').toUpperCase();
}

function authHeaders(includeJson = false) {
    const headers = {};
    if (includeJson) headers['Content-Type'] = 'application/json';
    if (currentUser?.token) headers.Authorization = `Bearer ${currentUser.token}`;
    return headers;
}

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    loadUser();
    updateNavBar();
    setupEventListeners();
    if (currentUser) {
        showSection('home');
        loadProducts();
        loadCartFromServer();   // <-- khôi phục cart từ server sau khi reload
    } else {
        showSection('auth');
    }
    updateCartUI();
});

// Setup Event Listeners
function setupEventListeners() {
    document.getElementById('loginForm').addEventListener('submit', handleLogin);
    document.getElementById('registerForm').addEventListener('submit', handleRegister);
    document.getElementById('checkoutForm').addEventListener('submit', handleCheckout);
    document.getElementById('paymentForm').addEventListener('submit', handlePayment);

    document.getElementById('toRegisterLink').addEventListener('click', toggleForm);
    document.getElementById('toLoginLink').addEventListener('click', toggleForm);

    document.getElementById('homeBtn').addEventListener('click', () => {
        if (!currentUser) {
            showSection('auth');
        } else {
            showSection('home');
            loadProducts();
        }
    });

    document.getElementById('cartBtn').addEventListener('click', () => {
        if (!currentUser) {
            showAuthMessage('Vui lòng đăng nhập trước', 'info');
            showSection('auth');
            return;
        }
        showSection('cart');
    });

    document.getElementById('ordersBtn').addEventListener('click', () => {
        if (!currentUser) {
            showAuthMessage('Vui lòng đăng nhập trước', 'info');
            showSection('auth');
            return;
        }
        showSection('orders');
        loadOrders();
    });

    document.getElementById('logoutBtn').addEventListener('click', logout);
    document.getElementById('brandBtn').addEventListener('click', (event) => {
        event.preventDefault();
        showSection(currentUser ? 'home' : 'auth');
    });
}

// Authentication
async function handleLogin(e) {
    e.preventDefault();
    const email = document.getElementById('loginEmail').value;
    const password = document.getElementById('loginPassword').value;

    try {
        const response = await fetch(`${API_BASE_URL}/users/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password })
        });

        if (!response.ok) {
            throw new Error('Đăng nhập thất bại');
        }

        const data = await response.json();
        currentUser = data;
        localStorage.setItem('user', JSON.stringify(data));

        showAuthMessage('Đăng nhập thành công!', 'success');
        document.getElementById('loginForm').reset();
        updateNavBar();
        await loadCartFromServer();   // load cart ngay sau khi đăng nhập
        setTimeout(() => {
            showSection('home');
            loadProducts();
        }, 1000);
    } catch (error) {
        showAuthMessage(error.message, 'error');
    }
}

async function handleRegister(e) {
    e.preventDefault();
    const email = document.getElementById('registerEmail').value;
    const password = document.getElementById('registerPassword').value;
    const fullName = document.getElementById('registerName').value;
    const phone = document.getElementById('registerPhone').value;

    try {
        const response = await fetch(`${API_BASE_URL}/users/register`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                email,
                password,
                fullName,
                phoneNumber: phone
            })
        });

        if (!response.ok) {
            throw new Error('Đăng ký thất bại');
        }

        showAuthMessage('Đăng ký thành công! Vui lòng đăng nhập.', 'success');
        document.getElementById('registerForm').reset();
        toggleForm();
    } catch (error) {
        showAuthMessage(error.message, 'error');
    }
}

function logout() {
    currentUser = null;
    localStorage.removeItem('user');
    cart = { items: [], totalPrice: 0 };
    showSection('auth');
    updateNavBar();
    document.getElementById('checkoutForm').reset();
    document.getElementById('paymentForm').reset();
    updateCartUI();
    showAuthMessage('Đã đăng xuất', 'info');
}

function loadUser() {
    try {
        const cached = localStorage.getItem('user');
        if (!cached) return;
        const parsed = JSON.parse(cached);
        if (!parsed || !Number.isInteger(parsed.id) || parsed.id <= 0 || typeof parsed.token !== 'string' || !parsed.token) {
            throw new Error('Invalid cached session');
        }
        currentUser = parsed;
        updateNavBar();
    } catch (_) {
        currentUser = null;
        try { localStorage.removeItem('user'); } catch (_) { /* Storage may be unavailable. */ }
    }
}

function updateNavBar() {
    const authSection = document.getElementById('authSection');
    const userInfo = document.getElementById('userInfo');
    const logoutBtn = document.getElementById('logoutBtn');

    if (currentUser) {
        authSection.style.display = 'none';
        userInfo.style.display = 'block';
        userInfo.innerHTML = `
            <span class="user-avatar">${escapeHtml(getInitials(currentUser.fullName))}</span>
            <span class="user-copy"><span>${escapeHtml(currentUser.fullName || 'Khách hàng')}</span><small>${escapeHtml(currentUser.email || '')}</small></span>
        `;
        logoutBtn.style.display = 'block';
    } else {
        authSection.style.display = 'flex';
        userInfo.style.display = 'none';
        logoutBtn.style.display = 'none';
    }
}

// Products
async function loadCartFromServer() {
    if (!currentUser) return;
    try {
        const response = await fetch(`${API_BASE_URL}/cart/user/${currentUser.id}`, {
            headers: authHeaders()
        });
        if (response.ok) {
            const cartData = await response.json();
            cart = cartData;
            updateCartUI();
        }
    } catch (error) {
        console.warn('Could not load cart from server:', error);
    }
}

async function loadProducts() {
    try {
        const response = await fetch(`${API_BASE_URL}/products`);
        if (!response.ok) throw new Error('Không thể tải sản phẩm');

        const products = await response.json();
        displayProducts(products);
    } catch (error) {
        console.error(error);
        showMessage('Lỗi khi tải sản phẩm', 'error');
    }
}

function displayProducts(products) {
    const grid = document.getElementById('productsGrid');
    grid.innerHTML = '';

    products.forEach((product, index) => {
        const visual = foodStyles[index % foodStyles.length];
        const card = document.createElement('div');
        card.className = 'product-card';
        card.innerHTML = `
            <div class="product-visual" style="--food-a:${visual.a};--food-b:${visual.b}">
                <span class="product-tag">${escapeHtml(product.category || 'Món ăn')}</span>
                <span class="food-glyph" aria-hidden="true">${visual.glyph}</span>
            </div>
            <div class="product-info">
                <div class="product-topline">
                    <div class="product-name">${escapeHtml(product.name)}</div>
                    <div class="product-price">${formatCurrency(product.price)}</div>
                </div>
                <div class="product-description">${escapeHtml(product.description || 'Món ngon được chuẩn bị tươi mới mỗi ngày.')}</div>
                <div class="product-meta"><span class="stock-dot">Còn ${product.stock} phần</span><span>15–30 phút</span></div>
                <div class="product-actions">
                    <input type="number" class="qty-input" value="1" min="1" max="${product.stock}">
                    <button class="btn-add-cart" onclick="addToCart(${product.id}, this)">+ Thêm vào giỏ</button>
                </div>
            </div>
        `;
        grid.appendChild(card);
    });
}

async function addToCart(productId, button) {
    if (!currentUser) {
        showMessage('Vui lòng đăng nhập trước', 'info');
        return;
    }

    const qtyInput = button.parentElement.querySelector('.qty-input');
    const quantity = parseInt(qtyInput.value);

    try {
        const response = await fetch(
            `${API_BASE_URL}/cart/user/${currentUser.id}/add`,
            {
                method: 'POST',
                headers: authHeaders(true),
                body: JSON.stringify({ productId, quantity })
            }
        );

        if (!response.ok) throw new Error('Không thể thêm vào giỏ');

        const cartData = await response.json();
        cart = cartData;
        updateCartUI();
        showMessage('Đã thêm vào giỏ hàng!', 'success');
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

// Cart
function updateCartUI() {
    const cartItems = document.getElementById('cartItems');
    const cartEmpty = document.getElementById('cartEmpty');
    const cartTotal = document.getElementById('cartTotal');
    const cartItemsDetail = document.getElementById('cartItemsDetail');
    const cartTotalDetail = document.getElementById('cartTotalDetail');
    const totalQuantity = (cart.items || []).reduce((sum, item) => sum + Number(item.quantity || 0), 0);
    document.getElementById('navCartCount').textContent = totalQuantity;
    document.getElementById('sideCartCount').textContent = `${totalQuantity} món`;
    document.getElementById('cartPageBadge').textContent = `${totalQuantity} món`;

    if (!cart.items || cart.items.length === 0) {
        if (cartItems) {
            cartItems.innerHTML = '';
            cartEmpty.style.display = 'block';
            cartTotal.innerHTML = '<span>Tạm tính</span><strong>0 VNĐ</strong>';
        }
        if (cartItemsDetail) {
            cartItemsDetail.innerHTML = '<div class="cart-empty"><strong>Chưa có sản phẩm</strong><small>Hãy quay lại menu để chọn món.</small></div>';
            cartTotalDetail.innerHTML = '<span>Tổng cộng</span><strong>0 VNĐ</strong>';
        }
        return;
    }

    cartEmpty.style.display = 'none';

    const itemsHtml = cart.items.map((item, index) => `
        <div class="cart-item">
            <div class="cart-item-thumb">${foodStyles[index % foodStyles.length].glyph}</div>
            <div><div class="cart-item-name">${escapeHtml(item.productName)}</div><div class="cart-item-meta">Số lượng: ${item.quantity}</div></div>
            <div><div class="cart-item-price">${formatCurrency(item.priceAtAdd * item.quantity)}</div><button class="cart-item-remove" onclick="removeFromCart(${item.id})">Xóa</button></div>
        </div>
    `).join('');

    if (cartItems) {
        cartItems.innerHTML = itemsHtml;
        cartTotal.innerHTML = `<span>Tạm tính</span><strong>${formatCurrency(cart.totalPrice)}</strong>`;
    }

    if (cartItemsDetail) {
        cartItemsDetail.innerHTML = cart.items.map(item => `
            <div class="detail-cart-item">
                <div><strong>${escapeHtml(item.productName)}</strong><small>Số lượng: ${item.quantity} · ${formatCurrency(item.priceAtAdd)}/phần</small></div>
                <span>${formatCurrency(item.priceAtAdd * item.quantity)}</span>
            </div>
        `).join('');
        cartTotalDetail.innerHTML = `<span>Tổng cộng</span><strong>${formatCurrency(cart.totalPrice)}</strong>`;
        document.getElementById('checkoutTotal').textContent = formatCurrency(cart.totalPrice);
    }
}

async function removeFromCart(itemId) {
    if (!currentUser) return;

    try {
        const response = await fetch(
            `${API_BASE_URL}/cart/${cart.id}/item/${itemId}`,
            { method: 'DELETE', headers: authHeaders() }
        );

        if (!response.ok) throw new Error('Không thể xóa');

        const cartData = await response.json();
        cart = cartData;
        updateCartUI();
        showMessage('Đã xóa khỏi giỏ', 'info');
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

// Checkout & Orders
async function readApiError(response, fallback) {
    const text = await response.text();
    let parsed;
    try { parsed = JSON.parse(text); } catch (_) { return { message: text || fallback }; }
    if (typeof parsed === 'string') return { message: parsed || fallback };
    if (!parsed || typeof parsed !== 'object') return { message: fallback };
    return { ...parsed, message: parsed.message || parsed.title || fallback };
}

function waitForCheckoutRetry(milliseconds, signal) {
    return new Promise((resolve, reject) => {
        signal?.throwIfAborted();
        const aborted = () => { clearTimeout(timer); reject(signal.reason); };
        const timer = setTimeout(() => { signal?.removeEventListener('abort', aborted); resolve(); }, milliseconds);
        signal?.addEventListener('abort', aborted, { once: true });
    });
}

async function createOrderWithRetry(payload, key, { signal, fetchOrder = window.fetch.bind(window), wait = waitForCheckoutRetry, random = Math.random } = {}) {
    if (!key) throw new Error('Thiếu mã yêu cầu đặt đơn.');
    for (let attempt = 0; attempt < 4; attempt++) {
        signal?.throwIfAborted();
        const response = await fetchOrder(`${API_BASE_URL}/orders`, {
            method: 'POST', headers: { ...authHeaders(true), 'Idempotency-Key': key }, body: payload, signal
        });
        if (response.status !== 409 || attempt === 3) return response;
        let error;
        try { error = await response.clone().json(); } catch (_) { return response; }
        if (error.code !== 'ORDER_CONCURRENT_WRITE' || error.retryable !== true) return response;
        // Equal jitter: 100..200, 200..400, 400..800 ms; four attempts total.
        const ceiling = 200 * (2 ** attempt);
        const delay = Math.floor(ceiling / 2 + random() * ceiling / 2);
        await wait(delay, signal);
    }
}

let checkoutInFlight = false;
function clearCompletedCheckout(orderId) {
    const storageKey = `order-create:${currentUser.id}`;
    const attempt = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
    if (attempt && attempt.orderId === orderId) {
        sessionStorage.removeItem(storageKey);
        document.getElementById('checkoutForm').reset();
    }
}
async function handleCheckout(e) {
    e.preventDefault();
    if (checkoutInFlight) return;

    if (!cart.items || cart.items.length === 0) {
        showMessage('Giỏ hàng trống!', 'error');
        return;
    }

    const address = document.getElementById('address').value;
    const city = document.getElementById('city').value;
    const orderItems = cart.items.map(item => ({
        productId: item.productId,
        quantity: item.quantity,
        unitPrice: item.priceAtAdd
    }));

    // Tính tổng từ items nếu cart.totalPrice là 0 hoặc undefined
    const totalAmount = (cart.totalPrice && cart.totalPrice > 0)
        ? cart.totalPrice
        : orderItems.reduce((sum, item) => sum + (item.unitPrice * item.quantity), 0);

    if (totalAmount <= 0) {
        showMessage('Không thể tính tổng tiền. Vui lòng thêm sản phẩm vào giỏ lại.', 'error');
        return;
    }

    let deadline;
    try {
        checkoutInFlight = true;
        const payload = JSON.stringify({ customerId: currentUser.id, totalAmount, address, city, items: orderItems });
        const storageKey = `order-create:${currentUser.id}`;
        let attempt = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
        if (attempt && attempt.payload !== payload && !attempt.orderId) {
            showMessage('Yêu cầu đặt đơn trước chưa rõ kết quả. Hãy kiểm tra lịch sử đơn hoặc gửi lại đúng nội dung cũ trước khi đổi đơn.', 'error');
            return;
        }
        if (!attempt || attempt.payload !== payload) {
            attempt = { key: crypto.randomUUID(), payload };
            sessionStorage.setItem(storageKey, JSON.stringify(attempt));
        }
        showMessage('Đang tạo đơn hàng...', 'info');
        const budget = new AbortController();
        deadline = setTimeout(() => budget.abort(new Error('Chưa nhận được kết quả đặt đơn. Hãy gửi lại đúng yêu cầu cũ hoặc kiểm tra lịch sử đơn.')), 8000);
        const response = await createOrderWithRetry(payload, attempt.key, { signal: budget.signal });

        if (!response.ok) {
            if ([400, 413].includes(response.status) && !attempt.orderId) {
                sessionStorage.removeItem(storageKey);
            }
            const errData = await readApiError(response, 'Không thể tạo đơn hàng');
            if (errData.retryable === false && ['OUT_OF_STOCK', 'PRODUCT_UNAVAILABLE'].includes(errData.code) && !attempt.orderId) {
                sessionStorage.removeItem(storageKey);
            }
            throw new Error(errData.message);
        }

        const order = await response.json();
        attempt.orderId = order.id;
        sessionStorage.setItem(storageKey, JSON.stringify(attempt));
        showMessage('Tạo đơn hàng thành công!', 'success');

        // Keep the original payload until payment settles so a second submit reuses its key.

        // Go to payment
        setTimeout(() => {
            goToPayment(order.id, order.totalAmount);
        }, 1000);
    } catch (error) {
        showMessage(error.message, 'error');
    } finally {
        clearTimeout(deadline);
        checkoutInFlight = false;
    }
}

function goToPayment(orderId, amount) {
    document.getElementById('paymentOrderId').value = orderId;
    document.getElementById('paymentAmountValue').value = amount;  // raw number
    document.getElementById('paymentAmount').textContent = amount.toLocaleString('vi-VN') + ' VNĐ';
    showSection('payment');
}

let paymentInFlight = false;
async function handlePayment(e) {
    e.preventDefault();
    if (paymentInFlight) return;

    const orderId = parseInt(document.getElementById('paymentOrderId').value);
    const paymentAmount = parseFloat(document.getElementById('paymentAmountValue').value) || cart.totalPrice;
    if (!Number.isInteger(orderId) || orderId <= 0 || !Number.isFinite(paymentAmount) || paymentAmount <= 0) {
        showMessage('Hãy chọn một đơn hàng hợp lệ trước khi thanh toán.', 'error');
        return;
    }
    const paymentKeyStorage = `payment-idempotency-${orderId}`;
    let paymentIdempotencyKey = sessionStorage.getItem(paymentKeyStorage);
    if (!paymentIdempotencyKey) {
        paymentIdempotencyKey = crypto.randomUUID();
        sessionStorage.setItem(paymentKeyStorage, paymentIdempotencyKey);
    }

    try {
        paymentInFlight = true;
        showMessage('Đang xử lý thanh toán...', 'info');
        const response = await fetch(`${API_BASE_URL}/payments`, {
            method: 'POST',
            headers: {
                ...authHeaders(true),
                'Idempotency-Key': paymentIdempotencyKey
            },
            body: JSON.stringify({
                orderId,
                amount: paymentAmount
            })
        });

        if (!response.ok) {
            const error = await readApiError(response, 'Thanh toán thất bại');
            throw new Error(error.message || 'Thanh toán thất bại');
        }

        const payment = await response.json();
        const paymentStatus = String(payment.status || '').toUpperCase();
        if (paymentStatus === 'FAILED') {
            sessionStorage.removeItem(paymentKeyStorage);
            clearCompletedCheckout(orderId);
            showMessage('Thanh toán bị từ chối. Đơn đã chuyển sang thất bại và tồn kho được hoàn lại.', 'error');
            return;
        }
        if (!['SUCCESS', 'UNKNOWN', 'PROCESSING'].includes(paymentStatus)) {
            showMessage(`Thanh toán đang ở trạng thái ${paymentStatus || 'không xác định'}.`, 'info');
            return;
        }

        if (paymentStatus === 'SUCCESS') {
            sessionStorage.removeItem(paymentKeyStorage);
            clearCompletedCheckout(orderId);
            showMessage('Thanh toán thành công!', 'success');
        } else {
            showMessage('Chưa xác định được kết quả thanh toán. Hệ thống sẽ đối soát bằng mã yêu cầu cũ; không thanh toán lại.', 'info');
        }

        document.getElementById('paymentForm').reset();

        // Lưu cart.id trước khi reset, rồi gọi clear API
        const cartIdToClear = cart.id;
        console.log('[Payment] cartIdToClear =', cartIdToClear);
        cart = { items: [], totalPrice: 0 };
        updateCartUI();

        if (cartIdToClear) {
            try {
                const clearRes = await fetch(`${API_BASE_URL}/cart/${cartIdToClear}/clear`, {
                    method: 'DELETE',
                    headers: authHeaders()
                });
                console.log('[Payment] Cart cleared, status =', clearRes.status);
            } catch (e) {
                console.warn('[Payment] Could not clear cart on server:', e);
            }
        } else {
            console.warn('[Payment] cartIdToClear is falsy - cart was not cleared on server!');
        }

        setTimeout(() => {
            showSection('orders');
            loadOrders();
        }, 1500);
    } catch (error) {
        showMessage(error.message, 'error');
    } finally {
        paymentInFlight = false;
    }
}

async function loadOrders() {
    if (!currentUser) return;

    try {
        const response = await fetch(`${API_BASE_URL}/orders/mine`, { headers: authHeaders() });
        if (!response.ok) throw new Error('Không thể tải đơn hàng');

        const orders = await response.json();
        displayOrders(orders);
    } catch (error) {
        console.error(error);
        showMessage('Lỗi khi tải đơn hàng', 'error');
    }
}

function displayOrders(orders) {
    const container = document.getElementById('ordersContainer');

    if (orders.length === 0) {
        container.innerHTML = '<div class="surface cart-empty"><span class="empty-icon">↗</span><strong>Chưa có đơn hàng nào</strong><small>Đơn hàng sau khi đặt sẽ xuất hiện tại đây.</small></div>';
        return;
    }

    container.innerHTML = orders.map(order => `
        <div class="order-card">
            <div class="order-header">
                <div class="order-id">Đơn #${order.id}</div>
                <div class="order-status status-${escapeHtml(order.status.toLowerCase() || 'pending')}">${escapeHtml(order.status)}</div>
            </div>
            <div class="order-details">
                <div class="detail-item">
                    <div class="detail-label">Ngày tạo</div>
                    <div class="detail-value">${new Date(order.createdAt).toLocaleDateString('vi-VN')}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Địa chỉ</div>
                    <div class="detail-value">${escapeHtml(order.address || 'N/A')}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Thành phố</div>
                    <div class="detail-value">${escapeHtml(order.city || 'N/A')}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Tổng tiền</div>
                    <div class="detail-value">${formatCurrency(order.totalAmount)}</div>
                </div>
            </div>
            <div class="order-items">
                ${order.items && order.items.length > 0 ? 
                    order.items.map(item => `
                        <div class="order-item">
                            <span>${escapeHtml(item.productName)} × ${item.quantity}</span>
                            <span>${formatCurrency(item.unitPrice * item.quantity)}</span>
                        </div>
                    `).join('') 
                    : '<div>Không có sản phẩm</div>'
                }
            </div>
        </div>
    `).join('');
}

// UI Functions
function showSection(sectionId) {
    document.querySelectorAll('.section').forEach(el => el.classList.remove('active'));
    document.getElementById(sectionId).classList.add('active');

    document.querySelectorAll('.nav-btn').forEach(button => {
        button.classList.toggle('active', button.dataset.section === sectionId);
    });
    window.scrollTo({ top: 0, behavior: 'smooth' });

    if (sectionId === 'home' && currentUser) {
        loadProducts();
    }
}

function toggleForm() {
    const loginForm = document.getElementById('loginForm');
    const registerForm = document.getElementById('registerForm');
    const loginToggle = document.getElementById('loginToggle');
    const registerToggle = document.getElementById('registerToggle');

    const showingLogin = loginForm.style.display !== 'none';
    loginForm.style.display = showingLogin ? 'none' : 'block';
    registerForm.style.display = showingLogin ? 'block' : 'none';
    loginToggle.style.display = showingLogin ? 'none' : 'block';
    registerToggle.style.display = showingLogin ? 'block' : 'none';
    const authTitle = document.getElementById('authTitle');
    authTitle.textContent = showingLogin ? 'Tạo tài khoản mới' : 'Chào mừng trở lại';
}

function showMessage(message, type = 'info') {
    const section = document.querySelector('.section.active');
    let alertContainer = section.querySelector('.alert-container');

    if (!alertContainer) {
        alertContainer = document.createElement('div');
        alertContainer.className = 'alert-container';
        section.insertBefore(alertContainer, section.firstChild);
    }

    const alert = document.createElement('div');
    alert.className = `alert alert-${type}`;
    alert.textContent = message;
    alertContainer.appendChild(alert);

    setTimeout(() => alert.remove(), 4000);
}

function showAuthMessage(message, type = 'info') {
    const section = document.getElementById('auth');
    let alertContainer = section.querySelector('.alert-container');

    if (!alertContainer) {
        alertContainer = document.createElement('div');
        alertContainer.className = 'alert-container';
        section.insertBefore(alertContainer, section.firstChild);
    }

    const alert = document.createElement('div');
    alert.className = `alert alert-${type}`;
    alert.textContent = message;
    alertContainer.appendChild(alert);

    setTimeout(() => alert.remove(), 4000);
}
