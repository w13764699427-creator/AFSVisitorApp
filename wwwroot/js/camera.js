/**
 * VisitorApp · JS 摄像头模块
 * 使用 WebRTC (getUserMedia) 替代 MAUI 原生摄像头，避免原生 API 兼容性问题。
 * 身份证扫描仍使用 MAUI 原生 MediaPicker。
 */
window.VisitorApp = window.VisitorApp || {};

window.VisitorApp.Camera = (function () {
    let _stream = null;
    let _videoElement = null;

    /**
     * 启动摄像头，将视频流绑定到指定 id 的 <video> 元素。
     * @param {string} elementId - 视频元素的 id
     * @param {string} optionsJson - JSON 字符串 { facingMode, width, height }
     * @returns {Promise<{success: boolean, error?: string}>}
     */
    async function start(elementId, optionsJson) {
        // 如果已有流在运行，先停止
        stop();

        var options = optionsJson ? JSON.parse(optionsJson) : {};
        var videoElement = document.getElementById(elementId);
        if (!videoElement) {
            throw new Error('未找到视频元素');
        }

        var constraints = {
            video: {
                facingMode: options.facingMode || 'user',
                width: { ideal: options.width || 640 },
                height: { ideal: options.height || 480 }
            },
            audio: false
        };

        try {
            _stream = await navigator.mediaDevices.getUserMedia(constraints);
        } catch (err) {
            console.error('[Camera] getUserMedia error:', err);
            if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
                throw new Error('摄像头权限被拒绝，请在设置中允许访问摄像头');
            } else if (err.name === 'NotFoundError' || err.name === 'DevicesNotFoundError') {
                throw new Error('未检测到摄像头设备');
            } else if (err.name === 'NotReadableError' || err.name === 'TrackStartError') {
                throw new Error('摄像头被其他应用占用，请关闭其他程序后重试');
            } else if (err.name === 'OverconstrainedError') {
                throw new Error('摄像头不支持所需参数');
            } else {
                throw new Error(err.message || '摄像头启动失败');
            }
        }

        videoElement.srcObject = _stream;
        _videoElement = videoElement;

        // 等待视频元数据加载完成
        await new Promise(function (resolve, reject) {
            videoElement.onloadedmetadata = function () {
                videoElement.play().then(resolve).catch(reject);
            };
            setTimeout(function () { reject(new Error('视频加载超时')); }, 8000);
        });
    }

    /**
     * 停止摄像头流。
     */
    function stop() {
        if (_stream) {
            _stream.getTracks().forEach(function (track) {
                track.stop();
            });
            _stream = null;
        }
        if (_videoElement) {
            _videoElement.srcObject = null;
            _videoElement = null;
        }
    }

    /**
     * 从当前视频帧捕获一张照片（JPEG data URL）。
     * @param {string} elementId - 视频元素的 id
     * @param {number} quality - JPEG 质量 0~1，默认 0.92
     * @returns {string|null} base64 data URL，失败返回 null
     */
    function captureFrame(elementId, quality) {
        var videoElement = document.getElementById(elementId);
        if (!videoElement || !videoElement.videoWidth) {
            console.error('[Camera] captureFrame: video not ready');
            return null;
        }

        try {
            var canvas = document.createElement('canvas');
            canvas.width = videoElement.videoWidth;
            canvas.height = videoElement.videoHeight;
            var ctx = canvas.getContext('2d');
            ctx.drawImage(videoElement, 0, 0);
            return canvas.toDataURL('image/jpeg', quality || 0.92);
        } catch (err) {
            console.error('[Camera] captureFrame error:', err);
            return null;
        }
    }

    /**
     * 检查当前环境是否支持摄像头。
     * @returns {boolean}
     */
    function isSupported() {
        return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    }

    /**
     * 获取摄像头流是否正在运行。
     * @returns {boolean}
     */
    function isActive() {
        return _stream !== null && _stream.active;
    }

    // 公开 API
    return {
        start: start,
        stop: stop,
        captureFrame: captureFrame,
        isSupported: isSupported,
        isActive: isActive
    };
})();
