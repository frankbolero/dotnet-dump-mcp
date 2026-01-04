# dumpheap

Analyzes managed heap objects, identifies memory leaks, and traces object references.

## Usage
```
dumpheap [options]
```

## Switches
*   `-stat`: Restricts the output to a statistical type summary.
*   `-strings`: Restricts the output to a statistical string value summary.
*   `-short`: Limits output to just the address of each object.
*   `-min <size>`: Ignores objects smaller than `size` bytes.
*   `-max <size>`: Ignores objects larger than `size` bytes.
*   `-mt <MethodTable address>`: Filters objects by MethodTable address.
*   `-type <partial type name>`: Filters objects by partial type name.
*   `[start [end]]`: Specifies an address range.

## Example
```
> dumpheap -stat
```
