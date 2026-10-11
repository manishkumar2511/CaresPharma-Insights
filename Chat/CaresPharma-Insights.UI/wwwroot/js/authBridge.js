window.carespharmaAuthBridge = {
    // 1. Resolve dynamic CaresPharma base URL based on active domain
    resolveCaresPharmaUrl: function (customUrl) {
        const hostname = window.location.hostname;
        if (hostname === 'localhost' || hostname === '127.0.0.1') {
            return 'http://localhost:4200';
        }
        if (hostname.includes('dev-') || hostname.includes('netlify.app')) {
            return 'https://dev-carespharma.netlify.app';
        }
        if (customUrl && customUrl.startsWith('http')) {
            return customUrl.replace(/\/+$/, '');
        }
        return 'https://carespharma.com';
    },

    // 2. Resolve dynamic CaresPharma API Base URL (Source of media & auth)
    resolveCaresPharmaApiUrl: function (customApiUrl) {
        const hostname = window.location.hostname;
        if (hostname === 'localhost' || hostname === '127.0.0.1') {
            return 'https://localhost:7163';
        }
        if (hostname.includes('dev-') || hostname.includes('netlify.app')) {
            return 'https://dev-carespharma.runasp.net';
        }
        if (customApiUrl && customApiUrl.startsWith('http')) {
            return customApiUrl.replace(/\/+$/, '');
        }
        return 'https://carespharma-api.runasp.net';
    },

    // 3. Resolve Media/Avatar Image URLs using actual active CaresPharma-Api environment
    resolveImageUrl: function (url) {
        if (!url || typeof url !== 'string') return null;
        if (url.startsWith('http://') || url.startsWith('https://')) return url;
        
        const apiHost = window.carespharmaAuthBridge.resolveCaresPharmaApiUrl();
        const cleanPath = url.startsWith('/') ? url : `/${url}`;
        return `${apiHost}${cleanPath}`;
    },

    // 4. Token and User storage helpers
    getToken: function () {
        const urlParams = new URLSearchParams(window.location.search);
        const queryToken = urlParams.get('token');
        if (queryToken && queryToken.length > 10) {
            window.history.replaceState({}, document.title, window.location.pathname);
            sessionStorage.setItem('carespharma_access_token', queryToken);
            return queryToken;
        }

        return sessionStorage.getItem('carespharma_access_token') 
            || localStorage.getItem('carespharma_access_token') 
            || localStorage.getItem('auth_access_token');
    },

    setToken: function (token, userInfo) {
        if (token) {
            sessionStorage.setItem('carespharma_access_token', token);
            localStorage.setItem('carespharma_access_token', token);
        }
        if (userInfo) {
            const userStr = typeof userInfo === 'string' ? userInfo : JSON.stringify(userInfo);
            sessionStorage.setItem('carespharma_user_info', userStr);
            localStorage.setItem('carespharma_user_info', userStr);
        }
    },

    clearToken: function () {
        sessionStorage.removeItem('carespharma_access_token');
        localStorage.removeItem('carespharma_access_token');
        sessionStorage.removeItem('carespharma_user_info');
        localStorage.removeItem('carespharma_user_info');
    },

    // 5. Retrieve user profile (first check stored user info, then fallback to JWT claims)
    getUserProfile: function (token) {
        // Priority 1: Check cached user profile (from CaresPharma auth_user)
        const storedUser = sessionStorage.getItem('carespharma_user_info') 
            || localStorage.getItem('carespharma_user_info')
            || localStorage.getItem('auth_user');

        if (storedUser) {
            try {
                const u = JSON.parse(storedUser);
                if (u && (u.firstName || u.email || u.fullName)) {
                    return {
                        id: u.id || '',
                        firstName: u.firstName || '',
                        lastName: u.lastName || '',
                        email: u.email || '',
                        role: u.role || 'Admin',
                        profileImageUrl: window.carespharmaAuthBridge.resolveImageUrl(u.profileImageUrl || u.profilePictureUrl),
                        pharmacyOwnerId: u.pharmacyOwnerId || u.ownerId || null
                    };
                }
            } catch (e) {
                console.warn('Could not parse storedUser', e);
            }
        }

        // Priority 2: Decode user profile directly from JWT payload claims
        if (!token) token = window.carespharmaAuthBridge.getToken();
        if (!token) return null;

        try {
            const base64Url = token.split('.')[1];
            if (!base64Url) return null;
            const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
            const jsonPayload = decodeURIComponent(window.atob(base64).split('').map(function(c) {
                return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
            }).join(''));

            const payload = JSON.parse(jsonPayload);
            const role = payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || 'Admin';
            const email = payload.email || payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'] || '';
            const rawImg = payload.userProfileImage || null;

            return {
                id: payload.nameid || payload.sub || payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] || '',
                firstName: payload.firstName || payload.unique_name || (email ? email.split('@')[0] : 'User'),
                lastName: payload.lastName || '',
                email: email,
                role: role,
                profileImageUrl: window.carespharmaAuthBridge.resolveImageUrl(rawImg),
                pharmacyOwnerId: payload.pharmacyOwnerId || payload.ownerId || null
            };
        } catch (e) {
            console.error('Failed to parse JWT payload', e);
            return null;
        }
    },

    // 6. Open Google-like Popup Window for CaresPharma SSO & wait for token postMessage
    openLoginPopup: function (configuredBaseUrl) {
        const baseUrl = window.carespharmaAuthBridge.resolveCaresPharmaUrl(configuredBaseUrl);
        const popupUrl = `${baseUrl}/account/login?mode=popup`;

        return new Promise((resolve, reject) => {
            const width = 500;
            const height = 650;
            const left = window.screenX + (window.outerWidth - width) / 2;
            const top = window.screenY + (window.outerHeight - height) / 2;
            const windowFeatures = `toolbar=no, menubar=no, width=${width}, height=${height}, top=${top}, left=${left}`;

            const popup = window.open(popupUrl, 'CaresPharmaSSO', windowFeatures);

            if (!popup || popup.closed || typeof popup.closed === 'undefined') {
                reject(new Error('Popup blocked by browser. Please allow popups for this site.'));
                return;
            }

            let resolved = false;

            function cleanup() {
                window.removeEventListener('message', onMessage);
                if (timer) clearInterval(timer);
            }

            function onMessage(event) {
                if (event.data && event.data.type === 'CARESPHARMA_AUTH_SUCCESS') {
                    const token = event.data.token;
                    const user = event.data.user;
                    if (token) {
                        resolved = true;
                        cleanup();
                        window.carespharmaAuthBridge.setToken(token, user);
                        resolve({ token: token, user: user });
                    }
                }
            }

            window.addEventListener('message', onMessage);

            // Check if popup was closed by user manually
            const timer = setInterval(() => {
                if (popup.closed) {
                    cleanup();
                    if (!resolved) {
                        resolve(null); // User closed popup without logging in
                    }
                }
            }, 500);
        });
    }
};
