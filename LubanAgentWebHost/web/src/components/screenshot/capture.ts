export function isCaptureSupported(): boolean {
	return typeof navigator !== 'undefined' && !!navigator.mediaDevices && typeof navigator.mediaDevices.getDisplayMedia === 'function';
}

/**
 * 抓取一帧屏幕静帧。
 * 必须在用户手势的调用栈内直接调用（前面不能有 await），否则浏览器会因失去用户激活而拒绝。
 * @param delay 授权通过后到取帧的等待时间（毫秒），用于避开共享授权框关闭时的残影
 */
export async function captureScreen(delay = 0): Promise<HTMLCanvasElement> {
	const stream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: 30 }, audio: false });
	try {
		// 授权框关闭瞬间系统画面可能还留着对话框残影，先等画面稳定再接流取帧
		if (delay > 0) await wait(delay);
		const video = document.createElement('video');
		video.muted = true;
		video.playsInline = true;
		video.srcObject = stream;
		await video.play();
		await waitFirstFrame(video);
		const width = video.videoWidth;
		const height = video.videoHeight;
		if (!width || !height) throw new Error('未能获取屏幕画面');
		const canvas = document.createElement('canvas');
		canvas.width = width;
		canvas.height = height;
		const ctx = canvas.getContext('2d');
		if (!ctx) throw new Error('当前浏览器不支持 Canvas 2D');
		ctx.drawImage(video, 0, 0, width, height);
		return canvas;
	} finally {
		stream.getTracks().forEach((track) => track.stop());
	}
}

function wait(ms: number): Promise<void> {
	return new Promise<void>((resolve) => window.setTimeout(resolve, ms));
}

function waitFirstFrame(video: HTMLVideoElement): Promise<void> {
	return new Promise<void>((resolve) => {
		if (video.readyState >= 2 && video.videoWidth > 0) {
			resolve();
			return;
		}
		let settled = false;
		const finish = (): void => {
			if (settled) return;
			settled = true;
			resolve();
		};
		video.addEventListener('loadeddata', finish, { once: true });
		const withCallback = video as HTMLVideoElement & { requestVideoFrameCallback?: (cb: () => void) => number };
		if (typeof withCallback.requestVideoFrameCallback === 'function') {
			withCallback.requestVideoFrameCallback(() => finish());
		}
		window.setTimeout(finish, 1200);
	});
}