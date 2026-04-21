// ═══════════════════════════════════════════════════════════
// SUN HOTEL - Client-side JavaScript
// ═══════════════════════════════════════════════════════════

// Auto-dismiss alerts after 5 seconds
document.addEventListener('DOMContentLoaded', function () {
    const alerts = document.querySelectorAll('.alert-dismissible');
    alerts.forEach(function (alert) {
        setTimeout(function () {
            var bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
            bsAlert.close();
        }, 5000);
    });

    // Auto-calculate service total
    const qtyInput = document.getElementById('Quantity');
    const priceInput = document.getElementById('UnitPrice');
    const totalDisplay = document.getElementById('totalDisplay');

    if (qtyInput && priceInput && totalDisplay) {
        function updateTotal() {
            const qty = parseInt(qtyInput.value) || 0;
            const price = parseFloat(priceInput.value) || 0;
            const total = qty * price;
            totalDisplay.textContent = total.toLocaleString('vi-VN') + ' VND';
        }
        qtyInput.addEventListener('input', updateTotal);
        priceInput.addEventListener('input', updateTotal);
    }

    // Booking form price calculation
    const checkInInput = document.querySelector('[name="CheckInDate"]');
    const checkOutInput = document.querySelector('[name="CheckOutDate"]');
    const roomTypeSelect = document.querySelector('[name="RoomTypeId"]');
    const priceDisplay = document.getElementById('estimatedPrice');

    if (checkInInput && checkOutInput && roomTypeSelect && priceDisplay) {
        function calculatePrice() {
            const checkIn = new Date(checkInInput.value);
            const checkOut = new Date(checkOutInput.value);
            if (checkIn && checkOut && checkOut > checkIn) {
                const nights = Math.ceil((checkOut - checkIn) / (1000 * 60 * 60 * 24));
                const selectedOption = roomTypeSelect.selectedOptions[0];
                const basePrice = parseFloat(selectedOption?.dataset?.price || 0);
                const total = nights * basePrice;
                priceDisplay.textContent = nights + ' dem x ' + basePrice.toLocaleString('vi-VN') + ' = ' + total.toLocaleString('vi-VN') + ' VND';
            }
        }
        checkInInput.addEventListener('change', calculatePrice);
        checkOutInput.addEventListener('change', calculatePrice);
        roomTypeSelect.addEventListener('change', calculatePrice);
    }

    // Confirm dangerous actions
    document.querySelectorAll('[data-confirm]').forEach(function (el) {
        el.addEventListener('click', function (e) {
            if (!confirm(el.dataset.confirm)) {
                e.preventDefault();
                return false;
            }
        });
    });

    // Tooltips
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });
});

// Format currency helper
function formatVND(amount) {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
}
