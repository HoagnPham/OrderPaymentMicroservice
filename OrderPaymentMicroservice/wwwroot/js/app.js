// API Configuration
const API_BASE_URL = 'http://localhost:5246/api';
let currentUser = null;
let cart = { items: [], totalPrice: 0 };

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
    updateCartUI();
    showAuthMessage('Đã đăng xuất', 'info');
}

function loadUser() {
    const user = localStorage.getItem('user');
    if (user) {
        currentUser = JSON.parse(user);
        updateNavBar();
    }
}

function updateNavBar() {
    const authSection = document.getElementById('authSection');
    const userInfo = document.getElementById('userInfo');
    const logoutBtn = document.getElementById('logoutBtn');

    if (currentUser) {
        authSection.style.display = 'none';
        userInfo.style.display = 'block';
        userInfo.textContent = `${currentUser.fullName} (${currentUser.email})`;
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

    products.forEach(product => {
        const card = document.createElement('div');
        card.className = 'product-card';
        card.innerHTML = `
            <div class="product-image">🍜</div>
            <div class="product-info">
                <div class="product-name">${product.name}</div>
                <div class="product-category">${product.category}</div>
                <div class="product-price">${product.price.toLocaleString()} VNĐ</div>
                <div class="product-stock">Còn: ${product.stock} cái</div>
                <div class="product-actions">
                    <input type="number" class="qty-input" value="1" min="1" max="${product.stock}">
                    <button class="btn-add-cart" onclick="addToCart(${product.id}, this)">Thêm giỏ</button>
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

    if (!cart.items || cart.items.length === 0) {
        if (cartItems) {
            cartItems.innerHTML = '';
            cartEmpty.style.display = 'block';
            cartTotal.innerHTML = '<div class="cart-total"><span>Tổng:</span><span>0 VNĐ</span></div>';
        }
        if (cartItemsDetail) {
            cartItemsDetail.innerHTML = '<div class="cart-empty">Giỏ hàng trống</div>';
            cartTotalDetail.innerHTML = '<div style="text-align: center; color: #999;">Không có sản phẩm</div>';
        }
        return;
    }

    cartEmpty.style.display = 'none';

    const itemsHtml = cart.items.map(item => `
        <div class="cart-item">
            <div class="cart-item-name">${item.productName}</div>
            <div class="cart-item-price">${item.priceAtAdd.toLocaleString()} VNĐ</div>
            <div class="cart-item-qty">Số lượng: ${item.quantity}</div>
            <button class="cart-item-remove" onclick="removeFromCart(${item.id})">Xóa</button>
        </div>
    `).join('');

    if (cartItems) {
        cartItems.innerHTML = itemsHtml;
        cartTotal.innerHTML = `<div class="cart-total"><span>Tổng:</span><span>${cart.totalPrice.toLocaleString()} VNĐ</span></div>`;
    }

    if (cartItemsDetail) {
        cartItemsDetail.innerHTML = cart.items.map(item => `
            <div class="order-item">
                <span>${item.productName} x${item.quantity}</span>
                <span>${(item.priceAtAdd * item.quantity).toLocaleString()} VNĐ</span>
            </div>
        `).join('');
        cartTotalDetail.innerHTML = `<div style="text-align: right; color: #667eea; font-size: 18px;">Tổng: ${cart.totalPrice.toLocaleString()} VNĐ</div>`;
        document.getElementById('checkoutTotal').textContent = cart.totalPrice.toLocaleString() + ' VNĐ';
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
async function handleCheckout(e) {
    e.preventDefault();

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

    try {
        showMessage('Đang tạo đơn hàng...', 'info');
        const response = await fetch(`${API_BASE_URL}/orders`, {
            method: 'POST',
            headers: authHeaders(true),
            body: JSON.stringify({
                customerId: currentUser.id,
                totalAmount,
                address,
                city,
                items: orderItems
            })
        });

        if (!response.ok) {
            let errMsg = 'Không thể tạo đơn hàng';
            try {
                const errData = await response.json();
                errMsg = errData.title || errData.message || errData || errMsg;
            } catch (_) { 
                errMsg = await response.text() || errMsg;
            }
            throw new Error(errMsg);
        }

        const order = await response.json();
        showMessage('Tạo đơn hàng thành công!', 'success');

        // Clear form
        document.getElementById('checkoutForm').reset();

        // Go to payment
        setTimeout(() => {
            goToPayment(order.id, order.totalAmount);
        }, 1000);
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

function goToPayment(orderId, amount) {
    document.getElementById('paymentOrderId').value = orderId;
    document.getElementById('paymentAmountValue').value = amount;  // raw number
    document.getElementById('paymentAmount').textContent = amount.toLocaleString('vi-VN') + ' VNĐ';
    showSection('payment');
}

async function handlePayment(e) {
    e.preventDefault();

    const orderId = parseInt(document.getElementById('paymentOrderId').value);
    const paymentAmount = parseFloat(document.getElementById('paymentAmountValue').value) || cart.totalPrice;
    const cardNumber = document.getElementById('cardNumber').value;
    const cardName = document.getElementById('cardName').value;
    const cardExpiry = document.getElementById('cardExpiry').value;
    const cardCvv = document.getElementById('cardCvv').value;

    try {
        showMessage('Đang xử lý thanh toán...', 'info');
        const response = await fetch(`${API_BASE_URL}/payments`, {
            method: 'POST',
            headers: {
                ...authHeaders(true),
                'Idempotency-Key': crypto.randomUUID()
            },
            body: JSON.stringify({
                orderId,
                amount: paymentAmount,
                paymentMethod: 'credit_card',
                cardDetails: {
                    cardNumber: cardNumber.slice(-4),
                    cardholderName: cardName,
                    expiryDate: cardExpiry,
                    cvv: cardCvv
                }
            })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Thanh toán thất bại');
        }

        const payment = await response.json();
        showMessage('Thanh toán thành công!', 'success');

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
        container.innerHTML = '<div class="cart-empty">Chưa có đơn hàng nào</div>';
        return;
    }

    container.innerHTML = orders.map(order => `
        <div class="order-card">
            <div class="order-header">
                <div class="order-id">Đơn #${order.id}</div>
                <div class="order-status status-${order.status.toLowerCase() || 'pending'}">${order.status}</div>
            </div>
            <div class="order-details">
                <div class="detail-item">
                    <div class="detail-label">Ngày tạo</div>
                    <div class="detail-value">${new Date(order.createdAt).toLocaleDateString('vi-VN')}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Địa chỉ</div>
                    <div class="detail-value">${order.address || 'N/A'}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Thành phố</div>
                    <div class="detail-value">${order.city || 'N/A'}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Tổng tiền</div>
                    <div class="detail-value">${order.totalAmount.toLocaleString()} VNĐ</div>
                </div>
            </div>
            <div class="order-items">
                ${order.items && order.items.length > 0 ? 
                    order.items.map(item => `
                        <div class="order-item">
                            <span>${item.productName} x${item.quantity}</span>
                            <span>${(item.unitPrice * item.quantity).toLocaleString()} VNĐ</span>
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
