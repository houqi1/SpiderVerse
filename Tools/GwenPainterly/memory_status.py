import ctypes,json
class Memory(ctypes.Structure):
    _fields_=[('length',ctypes.c_ulong),('load',ctypes.c_ulong)]+[(n,ctypes.c_ulonglong) for n in ['total_phys','avail_phys','total_page','avail_page','total_virtual','avail_virtual','avail_extended']]
m=Memory();m.length=ctypes.sizeof(m);ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
print(json.dumps({'available_commit_gb':round(m.avail_page/2**30,2),'available_ram_gb':round(m.avail_phys/2**30,2)}))
