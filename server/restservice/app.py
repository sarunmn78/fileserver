#!flask/bin/python
from flask import Flask, jsonify, request, make_response
from os import listdir
from os.path import isfile, join
import uuid
import json
import os
import time

app = Flask(__name__)

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

def get_transferfilename_id(file_id):
    fname = "";
    transfer_list = get_transferlist()
    fnames = [f["file_name"] for f in transfer_list if f["file_id"] == file_id]
    if len(fnames) > 0:
        fname = fnames[0]
        return fname
    return None;

def get_datafilename_id(file_id):
    fname = "";
    data_list = get_datafilelist()
    fnames = [f["file_name"] for f in data_list if f["file_id"] == file_id]
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

@app.route('/mydrive/initupload/<fname>', methods=['GET'])
def init_upload(fname):
    print fname
    transfer_filename = 'tempfiles/transferlist.json'
    transfer_list = get_transferlist()
    file_id = str(uuid.uuid4())
    transfer_list.append({"file_id" : file_id, "file_name" : fname})

    with open(transfer_filename, 'w') as outfile:
        json.dump(transfer_list, outfile)

    return jsonify({"file_id" : file_id})

@app.route('/mydrive/appenddata/<file_id>', methods=['POST'])
def append_to_file(file_id):
    data = request.get_data()
    fname = get_transferfilename_id(file_id)
    if fname == None:
        return jsonify({"error" : "file id is invalid"})
    
    time.sleep(1)
    with open("tempfiles/" + fname, 'ab') as outfile:
        outfile.write(data)
        outfile.close()
        return jsonify({"error" : "success"})

    return jsonify({"error" : "permission error"})

@app.route('/mydrive/uploaddone/<file_id>', methods=['GET'])
def upload_complete(file_id):    
    fname = get_transferfilename_id(file_id)
    if fname == None:
        return jsonify({"error" : "file id is invalid"})
    
    fsize = os.path.getsize("tempfiles/" + fname)
    # Move the file to data folder
    os.rename("tempfiles/" + fname, "data/" + fname)
    # Update the data filelist with this new file
    data_filelist = get_datafilelist()
    data_filelist.append({"file_id" : file_id, "file_name" : fname, "file_size" : fsize})
    with open('data/filelist.json', 'w') as outfile:
        json.dump(data_filelist, outfile)
    
    # Remove the item form the transfer file list
    remove_transferlist(file_id)
    return jsonify({"error" : "success"})

@app.route('/mydrive/pendingfileinfo/<file_id>', methods=['GET'])
def get_pendingtransfers(file_id):
    fname = get_transferfilename_id(file_id)
    if fname == None:
        return jsonify({"error" : "file id is invalid"})
    
    fsize = os.path.getsize("tempfiles/" + fname)
    return jsonify({"error":"success", "file_id": file_id, "file_size": fsize})

@app.route('/mydrive/download/<file_id>/<int:start_offset>/<int:size>', methods=['GET'])
def download_data(file_id, start_offset, size):
    print file_id
    data = request.get_data()
    fname = get_datafilename_id(file_id)
    if fname == None:
        print "filename is not found"
        return jsonify({"error" : "file id is invalid"})
    time.sleep(1)
    with open("data/" + fname, 'rb') as infile:
        infile.seek(start_offset, 0)
        data = infile.read(size)
        #print len(data)
        infile.close()
        response = make_response(data)
        response.headers['Content-Type'] = 'multipart/form-data'
        return response
    
    print "permission error"
    return jsonify({"error" : "permission error"})

@app.route('/mydrive/list', methods=['GET'])
def get_filelist():
    data_filelist = get_datafilelist()
    return jsonify(data_filelist)


if __name__ == '__main__':
    app.run(debug=True)

