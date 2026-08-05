// NexusOCR Session & AFK Inactivity Timeout Manager
(function () {
    // Determine if user is logged in
    function getCookie(name) {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) return parts.pop().split(';').shift();
        return null;
    }

    const isLoggedInAttr = document.body ? document.body.getAttribute("data-is-logged-in") : "false";
    const hasAuthCookie = !!getCookie("AuthToken");
    const isLoggedIn = isLoggedInAttr === "true" || (hasAuthCookie && !isAuthPage());

    function isAuthPage() {
        const path = window.location.pathname.toLowerCase();
        return path.includes("/login") ||
            path.includes("/register") ||
            path.includes("/forgotpassword") ||
            path.includes("/resetpassword") ||
            path === "/";
    }

    if (!isLoggedIn || isAuthPage()) {
        return;
    }

    // Configuration: 3 Minutes (180 seconds) AFK timeout
    const TOTAL_TIMEOUT_SECONDS = 180;
    const WARNING_THRESHOLD_SECONDS = 60; // Show warning modal when 60s remain (2 mins AFK)

    let lastActivityTimestamp = Date.now();
    let isWarningModalOpen = false;

    function formatMMSS(totalSecs) {
        if (totalSecs < 0) totalSecs = 0;
        const mins = Math.floor(totalSecs / 60);
        const secs = totalSecs % 60;
        return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }

    function resetActivity() {
        lastActivityTimestamp = Date.now();
        if (isWarningModalOpen) {
            closeWarningModal();
        }
        updateHeaderBadge(TOTAL_TIMEOUT_SECONDS);
    }

    // Attach activity listeners across window
    const events = ['mousemove', 'keydown', 'keypress', 'mousedown', 'touchstart', 'scroll', 'click'];
    events.forEach(function (evt) {
        window.addEventListener(evt, resetActivity, { passive: true });
    });

    function updateHeaderBadge(remainingSeconds) {
        const badgeTimerEl = document.getElementById('afkHeaderTimer');
        const badgeBoxEl = document.getElementById('afkStatusBadge');
        if (badgeTimerEl) {
            badgeTimerEl.textContent = formatMMSS(remainingSeconds);
        }
        if (badgeBoxEl) {
            if (remainingSeconds <= WARNING_THRESHOLD_SECONDS) {
                badgeBoxEl.classList.remove('bg-light', 'text-muted');
                badgeBoxEl.classList.add('bg-danger', 'text-white', 'animate-pulse');
            } else {
                badgeBoxEl.classList.remove('bg-danger', 'text-white', 'animate-pulse');
                badgeBoxEl.classList.add('bg-light', 'text-muted');
            }
        }
    }

    function injectWarningModal() {
        if (document.getElementById('afkWarningModal')) return;

        const modalHtml = `
        <div class="modal fade" id="afkWarningModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false" aria-hidden="true" style="z-index: 1060;">
            <div class="modal-dialog modal-dialog-centered">
                <div class="modal-content glass-panel border-warning shadow-lg">
                    <div class="modal-header border-0 bg-warning bg-opacity-10 text-dark pt-4 px-4">
                        <h5 class="modal-title fw-bold text-warning-emphasis d-flex align-items-center gap-2">
                            <i class="bi bi-clock-history text-warning fs-3"></i> Away From Keyboard (AFK) Warning
                        </h5>
                    </div>
                    <div class="modal-body text-center py-4 px-4">
                        <p class="text-secondary mb-2">You have been inactive for over 2 minutes.</p>
                        <p class="mb-3">To keep your account secure, your session will automatically expire in:</p>
                        <div class="display-3 fw-bold text-danger my-3" id="afkModalTimer">01:00</div>
                        <div class="progress mb-3" style="height: 6px;">
                            <div id="afkProgressBar" class="progress-bar bg-danger progress-bar-striped progress-bar-animated" role="progressbar" style="width: 100%;"></div>
                        </div>
                        <p class="small text-muted mb-0">Press any key, move your mouse, or click the button below to resume your session.</p>
                    </div>
                    <div class="modal-footer border-0 justify-content-center gap-2 pb-4">
                        <button type="button" class="btn btn-primary px-4 py-2 fw-semibold" id="afkResumeSessionBtn">
                            <i class="bi bi-check-circle-fill me-1"></i> I'm Back (Stay Logged In)
                        </button>
                        <a href="/Logout" class="btn btn-outline-secondary px-3 py-2">
                            Log Out Now
                        </a>
                    </div>
                </div>
            </div>
        </div>`;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const resumeBtn = document.getElementById('afkResumeSessionBtn');
        if (resumeBtn) {
            resumeBtn.addEventListener('click', function () {
                resetActivity();
            });
        }
    }

    function openWarningModal() {
        injectWarningModal();
        const modalEl = document.getElementById('afkWarningModal');
        if (modalEl && typeof bootstrap !== 'undefined') {
            const bsModal = bootstrap.Modal.getOrCreateInstance(modalEl);
            bsModal.show();
            isWarningModalOpen = true;
        }
    }

    function closeWarningModal() {
        const modalEl = document.getElementById('afkWarningModal');
        if (modalEl && typeof bootstrap !== 'undefined') {
            const bsModal = bootstrap.Modal.getInstance(modalEl);
            if (bsModal) {
                bsModal.hide();
            }
            isWarningModalOpen = false;
        }
    }

    function executeAutoLogout() {
        // Clear all cookies
        const cookies = ["AuthToken", "UserRole", "DisplayName", "DisplayEmail", "UserId"];
        cookies.forEach(c => {
            document.cookie = `${c}=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;`;
        });
        window.location.href = "/Login?reason=afk_timeout";
    }

    // Tick every 1000ms (1 second)
    setInterval(function () {
        const elapsedSecs = Math.floor((Date.now() - lastActivityTimestamp) / 1000);
        const remainingSecs = TOTAL_TIMEOUT_SECONDS - elapsedSecs;

        updateHeaderBadge(remainingSecs);

        if (remainingSecs <= 0) {
            executeAutoLogout();
            return;
        }

        if (remainingSecs <= WARNING_THRESHOLD_SECONDS) {
            if (!isWarningModalOpen) {
                openWarningModal();
            }
            const modalTimerEl = document.getElementById('afkModalTimer');
            if (modalTimerEl) {
                modalTimerEl.textContent = formatMMSS(remainingSecs);
            }
            const progressBarEl = document.getElementById('afkProgressBar');
            if (progressBarEl) {
                const pct = (remainingSecs / WARNING_THRESHOLD_SECONDS) * 100;
                progressBarEl.style.width = `${pct}%`;
            }
        } else {
            if (isWarningModalOpen) {
                closeWarningModal();
            }
        }
    }, 1000);

    // Initial badge setup
    document.addEventListener("DOMContentLoaded", function () {
        updateHeaderBadge(TOTAL_TIMEOUT_SECONDS);
    });
})();
