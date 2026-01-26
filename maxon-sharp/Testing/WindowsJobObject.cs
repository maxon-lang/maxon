using System.Runtime.InteropServices;

namespace MaxonSharp.Testing;

/// <summary>
/// Windows Job Object wrapper for managing child processes with guaranteed cleanup.
/// When the job object is disposed, all processes assigned to it are terminated.
/// </summary>
internal sealed class WindowsJobObject : IDisposable {
	private IntPtr _handle;
	private bool _disposed;

	public WindowsJobObject() {
		_handle = CreateJobObject(IntPtr.Zero, null);
		if (_handle == IntPtr.Zero) {
			throw new InvalidOperationException($"Failed to create job object: {Marshal.GetLastWin32Error()}");
		}

		// Configure job to kill all processes when job handle is closed
		var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
			BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION {
				LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
			}
		};

		int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
		IntPtr infoPtr = Marshal.AllocHGlobal(size);
		try {
			Marshal.StructureToPtr(info, infoPtr, false);
			if (!SetInformationJobObject(_handle, JobObjectInfoType.ExtendedLimitInformation, infoPtr, (uint)size)) {
				throw new InvalidOperationException($"Failed to set job object information: {Marshal.GetLastWin32Error()}");
			}
		} finally {
			Marshal.FreeHGlobal(infoPtr);
		}
	}

	public bool AssignProcess(IntPtr processHandle) {
		if (_disposed) throw new ObjectDisposedException(nameof(WindowsJobObject));
		return AssignProcessToJobObject(_handle, processHandle);
	}

	public void Dispose() {
		if (_disposed) return;
		_disposed = true;

		if (_handle != IntPtr.Zero) {
			CloseHandle(_handle);
			_handle = IntPtr.Zero;
		}
	}

	private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

	private enum JobObjectInfoType {
		ExtendedLimitInformation = 9
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IO_COUNTERS {
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
		public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
		public IO_COUNTERS IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr hObject);
}
