from os import listdir
from os.path import isfile, join
import uuid
import json
import os

def get_datafilelist():
    data_filelist = []
    data_filename = 'data/filelist.json'
    if os.path.exists(data_filename):
        with open(data_filename) as data_file:    
            data_filelist = json.load(data_file)
    return data_filelist        

def get_transferlist():
    transfer_list = []
    transfer_filename = 'tempfiles/transferlist.json'
    if os.path.exists(transfer_filename):
        with open(transfer_filename) as data_file:    
            transfer_list = json.load(data_file)
    return transfer_list        

def get_filename_id(fileid):
    fname = "";
    transfer_list = get_transferlist()
    fnames = [f["file_name"] for f in transfer_list if f["file_id"] == file_id]
    if len(fnames) > 0:
        fname = fnames[0]
        return fname
    return None;

def remove_transferlist(file_id):
    transfer_filename = 'tempfiles/transferlist.json'
    transfer_list = get_transferlist()
    for k in xrange(len(transfer_list)):
        if transfer_list[k]["file_id"] == file_id:
            del transfer_list[k]
            with open(transfer_filename, 'w') as outfile:
                json.dump(transfer_list, outfile)

def init_upload(fname):
    transfer_filename = 'tempfiles/transferlist.json'
    transfer_list = get_transferlist()
    file_id = str(uuid.uuid4())
    transfer_list.append({"file_id" : file_id, "file_name" : fname})

    with open(transfer_filename, 'w') as outfile:
        json.dump(transfer_list, outfile)

    return file_id

def append_to_file(file_id, data):
    fname = get_filename_id(file_id)
    if fname == None:
        return {"error" : "file id is invalid"}
    
    with open("tempfiles/" + fname, 'ab') as outfile:
        outfile.write(data)
        outfile.close()
        return {"error" : "success"}

    return {"error" : "permission error"}

def upload_complete(file_id):    
    fname = get_filename_id(file_id)
    if fname == None:
        return {"error" : "file id is invalid"}
    
    # Move the file to data folder
    os.rename("tempfiles/" + fname, "data/" + fname)
    # Update the data filelist with this new file
    data_filelist = get_datafilelist()
    data_filelist.append({"file_id" : file_id, "file_name" : fname})
    with open('data/filelist.json', 'w') as outfile:
        json.dump(data_filelist, outfile)
    
    # Remove the item form the transfer file list
    remove_transferlist(file_id)
    return {"error" : "success"}

file_id = init_upload("sample.jpg")
f = open("sample.jpg", 'rb')
while True:
    piece = f.read(1024)  
    if not piece:
        break
    print "append_to_file: " + str(len(piece))
    append_to_file(file_id, piece)
f.close()
upload_complete(file_id)